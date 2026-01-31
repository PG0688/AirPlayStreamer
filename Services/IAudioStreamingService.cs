namespace AirPlayStreamer.Services;

public interface IAudioStreamingService
{
    /// <summary>
    /// Get all available audio output devices
    /// </summary>
    Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync();

    /// <summary>
    /// Get all paired Bluetooth audio devices
    /// </summary>
    Task<IReadOnlyList<BluetoothAudioDevice>> GetBluetoothDevicesAsync();

    /// <summary>
    /// Create a multi-output device combining multiple audio outputs
    /// </summary>
    Task<bool> CreateMultiOutputDeviceAsync(string name, IEnumerable<string> deviceIds);

    /// <summary>
    /// Set the system audio output device
    /// </summary>
    Task<bool> SetSystemOutputAsync(string deviceId);

    /// <summary>
    /// Start streaming to an AirPlay device
    /// </summary>
    Task<bool> StartAirPlayStreamAsync(string deviceIp, int port);

    /// <summary>
    /// Stop streaming to an AirPlay device
    /// </summary>
    Task StopAirPlayStreamAsync();

    /// <summary>
    /// Connect to a Bluetooth audio device
    /// </summary>
    Task<bool> ConnectBluetoothAsync(string deviceAddress);

    /// <summary>
    /// Current streaming state
    /// </summary>
    StreamingState State { get; }

    event EventHandler<StreamingStateChangedEventArgs>? StateChanged;
}

public class AudioOutputDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AudioDeviceType Type { get; set; }
    public bool IsDefault { get; set; }
}

public class BluetoothAudioDevice
{
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
}

public enum AudioDeviceType
{
    BuiltIn,
    AirPlay,
    Bluetooth,
    USB,
    Aggregate,
    Other
}

public enum StreamingState
{
    Stopped,
    Connecting,
    Streaming,
    Error
}

public class StreamingStateChangedEventArgs : EventArgs
{
    public StreamingState State { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> ActiveDevices { get; init; } = Array.Empty<string>();
}
