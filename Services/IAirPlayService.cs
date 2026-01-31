using AirPlayStreamer.Models;

namespace AirPlayStreamer.Services;

public interface IAirPlayService
{
    bool SupportsNativePicker { get; }
    bool IsDiscovering { get; }
    IReadOnlyList<AirPlayDevice> DiscoveredDevices { get; }

    event EventHandler<AirPlayDevicesChangedEventArgs>? DevicesChanged;
    event EventHandler<AirPlayConnectionEventArgs>? ConnectionStateChanged;

    Task StartDiscoveryAsync(CancellationToken cancellationToken = default);
    void StopDiscovery();
    Task<bool> ConnectAsync(AirPlayDevice device, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    void ShowAirPlayPicker();
}

public class AirPlayDevicesChangedEventArgs : EventArgs
{
    public IReadOnlyList<AirPlayDevice> Devices { get; init; } = Array.Empty<AirPlayDevice>();
    public AirPlayDevice? AddedDevice { get; init; }
    public AirPlayDevice? RemovedDevice { get; init; }
}

public class AirPlayConnectionEventArgs : EventArgs
{
    public AirPlayDevice? Device { get; init; }
    public AirPlayConnectionState State { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum AirPlayConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}
