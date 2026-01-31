using AirPlayStreamer.Models;

namespace AirPlayStreamer.Services;

public abstract class AirPlayServiceBase : IAirPlayService
{
    protected readonly List<AirPlayDevice> _devices = new();
    protected CancellationTokenSource? _discoveryCts;

    public virtual bool SupportsNativePicker => false;
    public bool IsDiscovering { get; protected set; }
    public IReadOnlyList<AirPlayDevice> DiscoveredDevices => _devices.AsReadOnly();

    public event EventHandler<AirPlayDevicesChangedEventArgs>? DevicesChanged;
    public event EventHandler<AirPlayConnectionEventArgs>? ConnectionStateChanged;

    public abstract Task StartDiscoveryAsync(CancellationToken cancellationToken = default);
    public abstract void StopDiscovery();
    public abstract Task<bool> ConnectAsync(AirPlayDevice device, CancellationToken cancellationToken = default);
    public abstract Task DisconnectAsync();

    public virtual void ShowAirPlayPicker()
    {
        throw new NotSupportedException("Native AirPlay picker is not supported on this platform");
    }

    protected void OnDevicesChanged(AirPlayDevice? added = null, AirPlayDevice? removed = null)
    {
        DevicesChanged?.Invoke(this, new AirPlayDevicesChangedEventArgs
        {
            Devices = _devices.AsReadOnly(),
            AddedDevice = added,
            RemovedDevice = removed
        });
    }

    protected void OnConnectionStateChanged(AirPlayDevice? device, AirPlayConnectionState state, string? error = null)
    {
        ConnectionStateChanged?.Invoke(this, new AirPlayConnectionEventArgs
        {
            Device = device,
            State = state,
            ErrorMessage = error
        });
    }

    protected AirPlayDeviceType ParseDeviceType(Dictionary<string, string> txtRecords)
    {
        if (txtRecords.TryGetValue("model", out var model))
        {
            return model.ToLowerInvariant() switch
            {
                var m when m.Contains("appletv") => AirPlayDeviceType.AppleTV,
                var m when m.Contains("homepod") && m.Contains("mini") => AirPlayDeviceType.HomePodMini,
                var m when m.Contains("homepod") => AirPlayDeviceType.HomePod,
                var m when m.Contains("airport") => AirPlayDeviceType.AirPortExpress,
                _ => AirPlayDeviceType.ThirdParty
            };
        }
        return AirPlayDeviceType.Unknown;
    }
}
