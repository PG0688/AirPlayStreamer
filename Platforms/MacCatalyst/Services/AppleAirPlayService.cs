using AirPlayStreamer.Models;
using AirPlayStreamer.Services;
using Foundation;
using AVKit;
using UIKit;

namespace AirPlayStreamer.Platforms.MacCatalyst.Services;

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

#pragma warning disable CA1422 // NSNetServiceBrowser is deprecated but still functional
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
#pragma warning restore CA1422

        try
        {
            // Give devices more time to respond - AirPlay devices can be slow
            await Task.Delay(15000, _discoveryCts.Token);
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

#pragma warning disable CA1422
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
#pragma warning restore CA1422

        IsDiscovering = false;
    }

    private void OnServiceFound(object? sender, NSNetServiceEventArgs e)
    {
#pragma warning disable CA1422
        var service = e.Service;
        System.Diagnostics.Debug.WriteLine($"[AirPlay] Found service: {service.Name} ({service.Type})");
        service.Delegate = new NetServiceDelegate(this);
        _pendingServices.Add(service);
        service.Resolve(10.0);
#pragma warning restore CA1422
    }

    private void OnServiceResolved(NSNetService service)
    {
#pragma warning disable CA1422
        System.Diagnostics.Debug.WriteLine($"[AirPlay] Resolved: {service.Name} at {service.HostName}:{service.Port}");

        // RAOP services have names like "02F8A65274BB@Device Name" - extract the friendly name
        var serviceName = service.Name;
        var friendlyName = serviceName;
        if (serviceName.Contains('@'))
        {
            var parts = serviceName.Split('@', 2);
            if (parts.Length == 2)
            {
                friendlyName = parts[1]; // Get the part after @
            }
        }

        var device = new AirPlayDevice
        {
            Id = $"{service.Name}@{service.HostName}",
            Name = friendlyName,
            IPAddress = GetIPAddress(service),
            Port = (int)service.Port,
            TxtRecords = ParseTxtRecords(service.TxtRecordData)
        };
#pragma warning restore CA1422

        device.DeviceType = ParseDeviceType(device.TxtRecords);
        device.Model = device.TxtRecords.GetValueOrDefault("model", "Unknown");

        System.Diagnostics.Debug.WriteLine($"[AirPlay] Adding device: {device.Name} ({device.IPAddress}:{device.Port}) Model: {device.Model}");

        // Deduplicate by friendly name or IP address (same device may appear via RAOP and AirPlay services)
        if (!_devices.Any(d => d.Name == device.Name || (d.IPAddress == device.IPAddress && !string.IsNullOrEmpty(d.IPAddress))))
        {
            _devices.Add(device);
            MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged(added: device));
        }

        _pendingServices.Remove(service);
    }

    private void OnServiceRemoved(object? sender, NSNetServiceEventArgs e)
    {
#pragma warning disable CA1422
        var device = _devices.FirstOrDefault(d => d.Name == e.Service.Name);
#pragma warning restore CA1422
        if (device != null)
        {
            _devices.Remove(device);
            MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged(removed: device));
        }
    }

    private string GetIPAddress(NSNetService service)
    {
#pragma warning disable CA1422
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
#pragma warning restore CA1422
    }

    private Dictionary<string, string> ParseTxtRecords(NSData? txtData)
    {
        var result = new Dictionary<string, string>();
        if (txtData == null) return result;

#pragma warning disable CA1422
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
#pragma warning restore CA1422
        return result;
    }

    public override void ShowAirPlayPicker()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // Get the key window and root view controller
                var window = UIApplication.SharedApplication.KeyWindow
                    ?? UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow)
                    ?? UIApplication.SharedApplication.Windows.FirstOrDefault();

                if (window?.RootViewController?.View == null)
                {
                    System.Diagnostics.Debug.WriteLine("[AirPlay] No window available for picker");
                    return;
                }

                var rootView = window.RootViewController.View;

                // Create the route picker view
                var routePickerView = new AVRoutePickerView
                {
                    Frame = new CoreGraphics.CGRect(0, 0, 44, 44),
                    ActiveTintColor = UIColor.SystemBlue,
                    TintColor = UIColor.White,
                    Hidden = true // Hide it since we just need to trigger the picker
                };

                // Add to view hierarchy (required for the picker to work)
                rootView.AddSubview(routePickerView);

                // Find and trigger the button
                foreach (var subview in routePickerView.Subviews)
                {
                    if (subview is UIButton button)
                    {
                        button.SendActionForControlEvents(UIControlEvent.TouchUpInside);
                        break;
                    }
                }

                // Remove after a delay to allow picker to show
                Task.Delay(500).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() => routePickerView.RemoveFromSuperview());
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AirPlay] ShowAirPlayPicker error: {ex.Message}");
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
#pragma warning disable CA1422
            System.Diagnostics.Debug.WriteLine($"[AirPlay] Delegate AddressResolved: {sender.Name}");
#pragma warning restore CA1422
            _parent.OnServiceResolved(sender);
        }

        public override void ResolveFailure(NSNetService sender, NSDictionary errors)
        {
#pragma warning disable CA1422
            System.Diagnostics.Debug.WriteLine($"[AirPlay] Delegate ResolveFailure: {sender.Name} - {errors}");
#pragma warning restore CA1422
            _parent._pendingServices.Remove(sender);
        }
    }
}
