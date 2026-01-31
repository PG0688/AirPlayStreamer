using AirPlayStreamer.Models;
using AirPlayStreamer.Services;
using Foundation;
using AVKit;
using UIKit;

namespace AirPlayStreamer.Platforms.iOS.Services;

public class AppleAirPlayService : AirPlayServiceBase
{
    private NSNetServiceBrowser? _raopBrowser;
    private NSNetServiceBrowser? _airplayBrowser;
    private readonly List<NSNetService> _pendingServices = new();

    public override bool SupportsNativePicker => true;

    public override async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (IsDiscovering) return;

        _discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsDiscovering = true;
        _devices.Clear();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _raopBrowser = new NSNetServiceBrowser();
            _raopBrowser.FoundService += OnServiceFound;
            _raopBrowser.ServiceRemoved += OnServiceRemoved;
            _raopBrowser.SearchForServices("_raop._tcp", "local.");

            _airplayBrowser = new NSNetServiceBrowser();
            _airplayBrowser.FoundService += OnServiceFound;
            _airplayBrowser.ServiceRemoved += OnServiceRemoved;
            _airplayBrowser.SearchForServices("_airplay._tcp", "local.");
        });

        try
        {
            await Task.Delay(5000, _discoveryCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    public override void StopDiscovery()
    {
        _discoveryCts?.Cancel();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _raopBrowser?.Stop();
            _raopBrowser?.Dispose();
            _raopBrowser = null;

            _airplayBrowser?.Stop();
            _airplayBrowser?.Dispose();
            _airplayBrowser = null;

            foreach (var service in _pendingServices)
            {
                service.Dispose();
            }
            _pendingServices.Clear();
        });

        IsDiscovering = false;
    }

    private void OnServiceFound(object? sender, NSNetServiceEventArgs e)
    {
        var service = e.Service;
        service.Delegate = new NetServiceDelegate(this);
        _pendingServices.Add(service);
        service.Resolve(10.0);
    }

    private void OnServiceResolved(NSNetService service)
    {
        var device = new AirPlayDevice
        {
            Id = $"{service.Name}@{service.HostName}",
            Name = service.Name,
            IPAddress = GetIPAddress(service),
            Port = (int)service.Port,
            TxtRecords = ParseTxtRecords(service.TxtRecordData)
        };

        device.DeviceType = ParseDeviceType(device.TxtRecords);
        device.Model = device.TxtRecords.GetValueOrDefault("model", "Unknown");

        if (!_devices.Any(d => d.Equals(device)))
        {
            _devices.Add(device);
            MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged(added: device));
        }

        _pendingServices.Remove(service);
    }

    private void OnServiceRemoved(object? sender, NSNetServiceEventArgs e)
    {
        var device = _devices.FirstOrDefault(d => d.Name == e.Service.Name);
        if (device != null)
        {
            _devices.Remove(device);
            MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged(removed: device));
        }
    }

    private string GetIPAddress(NSNetService service)
    {
        if (service.Addresses == null || service.Addresses.Length == 0)
            return service.HostName ?? string.Empty;

        foreach (NSData addressData in service.Addresses)
        {
            var bytes = addressData.ToArray();
            if (bytes.Length >= 8 && bytes[1] == 2)
            {
                return $"{bytes[4]}.{bytes[5]}.{bytes[6]}.{bytes[7]}";
            }
        }
        return service.HostName ?? string.Empty;
    }

    private Dictionary<string, string> ParseTxtRecords(NSData? txtData)
    {
        var result = new Dictionary<string, string>();
        if (txtData == null) return result;

        var dict = NSNetService.DictionaryFromTxtRecord(txtData);
        if (dict != null)
        {
            foreach (var key in dict.Keys)
            {
                if (dict[key] is NSData valueData)
                {
                    result[key.ToString()] = NSString.FromData(valueData, NSStringEncoding.UTF8)?.ToString() ?? "";
                }
            }
        }
        return result;
    }

    public override void ShowAirPlayPicker()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var routePickerView = new AVRoutePickerView
            {
                Frame = new CoreGraphics.CGRect(0, 0, 40, 40),
                ActiveTintColor = UIColor.SystemBlue,
                TintColor = UIColor.Gray
            };

            foreach (var subview in routePickerView.Subviews)
            {
                if (subview is UIButton button)
                {
                    button.SendActionForControlEvents(UIControlEvent.TouchUpInside);
                    break;
                }
            }
        });
    }

    public override Task<bool> ConnectAsync(AirPlayDevice device, CancellationToken cancellationToken = default)
    {
        OnConnectionStateChanged(device, AirPlayConnectionState.Connected);
        return Task.FromResult(true);
    }

    public override Task DisconnectAsync()
    {
        OnConnectionStateChanged(null, AirPlayConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    private class NetServiceDelegate : NSNetServiceDelegate
    {
        private readonly AppleAirPlayService _parent;

        public NetServiceDelegate(AppleAirPlayService parent)
        {
            _parent = parent;
        }

        public override void AddressResolved(NSNetService sender)
        {
            _parent.OnServiceResolved(sender);
        }

        public override void ResolveFailure(NSNetService sender, NSDictionary errors)
        {
            _parent._pendingServices.Remove(sender);
        }
    }
}
