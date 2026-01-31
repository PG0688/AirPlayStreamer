using AirPlayStreamer.Models;
using AirPlayStreamer.Services;
using Zeroconf;

namespace AirPlayStreamer.Platforms.Windows.Services;

public class WindowsAirPlayService : AirPlayServiceBase
{
    private const string RaopServiceType = "_raop._tcp.local.";
    private const string AirPlayServiceType = "_airplay._tcp.local.";

    public override async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (IsDiscovering) return;

        _discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsDiscovering = true;
        _devices.Clear();
        MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged());

        try
        {
            var raopHosts = await ZeroconfResolver.ResolveAsync(
                RaopServiceType,
                TimeSpan.FromSeconds(5),
                cancellationToken: _discoveryCts.Token);

            ProcessDiscoveredHosts(raopHosts);

            var airplayHosts = await ZeroconfResolver.ResolveAsync(
                AirPlayServiceType,
                TimeSpan.FromSeconds(5),
                cancellationToken: _discoveryCts.Token);

            ProcessDiscoveredHosts(airplayHosts);
        }
        catch (OperationCanceledException)
        {
            // Discovery was cancelled
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private void ProcessDiscoveredHosts(IReadOnlyList<IZeroconfHost> hosts)
    {
        foreach (var host in hosts)
        {
            foreach (var service in host.Services.Values)
            {
                var txtRecords = service.Properties
                    .SelectMany(p => p)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var device = new AirPlayDevice
                {
                    Id = $"{host.DisplayName}@{host.IPAddress}",
                    Name = host.DisplayName,
                    IPAddress = host.IPAddress,
                    Port = service.Port,
                    TxtRecords = txtRecords
                };

                device.DeviceType = ParseDeviceType(device.TxtRecords);
                device.Model = device.TxtRecords.GetValueOrDefault("model", "Unknown");

                if (!_devices.Any(d => d.Equals(device)))
                {
                    _devices.Add(device);
                    MainThread.BeginInvokeOnMainThread(() => OnDevicesChanged(added: device));
                }
            }
        }
    }

    public override void StopDiscovery()
    {
        _discoveryCts?.Cancel();
        IsDiscovering = false;
    }

    public override async Task<bool> ConnectAsync(AirPlayDevice device, CancellationToken cancellationToken = default)
    {
        OnConnectionStateChanged(device, AirPlayConnectionState.Connecting);

        try
        {
            // Placeholder for actual RTSP/AirPlay protocol implementation
            await Task.Delay(500, cancellationToken);
            OnConnectionStateChanged(device, AirPlayConnectionState.Connected);
            return true;
        }
        catch (Exception ex)
        {
            OnConnectionStateChanged(device, AirPlayConnectionState.Failed, ex.Message);
            return false;
        }
    }

    public override Task DisconnectAsync()
    {
        OnConnectionStateChanged(null, AirPlayConnectionState.Disconnected);
        return Task.CompletedTask;
    }
}
