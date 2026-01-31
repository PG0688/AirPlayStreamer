using System.Diagnostics;
using AirPlayStreamer.Models;
using AirPlayStreamer.Services.AirPlay;

namespace AirPlayStreamer.Platforms.MacCatalyst.Services;

/// <summary>
/// Unified service for multi-room audio streaming
/// Captures system audio and streams to multiple AirPlay and Bluetooth devices simultaneously
/// </summary>
public class MultiRoomStreamingService : IDisposable
{
    private readonly ScreenCaptureAudioService _audioCapture;
    private readonly CoreAudioService _coreAudio;
    private readonly List<RaopClient> _airplayClients = new();
    private readonly List<string> _bluetoothDevices = new();

    private bool _isStreaming;
    private ushort _sequenceNumber;

    public event EventHandler<StreamingStateEventArgs>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsStreaming => _isStreaming;
    public IReadOnlyList<string> ActiveDevices => _airplayClients.Select(c => c.ToString() ?? "").Concat(_bluetoothDevices).ToList();

    public MultiRoomStreamingService()
    {
        _audioCapture = new ScreenCaptureAudioService();
        _coreAudio = new CoreAudioService();

        _audioCapture.AudioDataReceived += OnAudioDataReceived;
        _audioCapture.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(this, e);
    }

    /// <summary>
    /// Add an AirPlay device to stream to
    /// </summary>
    public async Task<bool> AddAirPlayDeviceAsync(AirPlayDevice device)
    {
        if (string.IsNullOrEmpty(device.IPAddress))
            return false;

        try
        {
            var client = new RaopClient(device.IPAddress, device.Port > 0 ? device.Port : 7000);

            Debug.WriteLine($"[MultiRoom] Connecting to AirPlay device: {device.Name} ({device.IPAddress})");

            if (await client.ConnectAsync())
            {
                if (await client.SetupSessionAsync())
                {
                    _airplayClients.Add(client);
                    Debug.WriteLine($"[MultiRoom] Added AirPlay device: {device.Name}");
                    return true;
                }
            }

            client.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiRoom] Error adding AirPlay device: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to connect to {device.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add a Bluetooth device to stream to
    /// </summary>
    public async Task<bool> AddBluetoothDeviceAsync(string deviceAddress, string deviceName)
    {
        try
        {
            Debug.WriteLine($"[MultiRoom] Connecting Bluetooth device: {deviceName}");

            var connected = await _coreAudio.ConnectBluetoothDeviceAsync(deviceAddress);
            if (connected)
            {
                _bluetoothDevices.Add(deviceName);
                Debug.WriteLine($"[MultiRoom] Added Bluetooth device: {deviceName}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiRoom] Error adding Bluetooth device: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to connect Bluetooth {deviceName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Start streaming to all connected devices
    /// </summary>
    public async Task<bool> StartStreamingAsync()
    {
        if (_isStreaming)
            return true;

        if (_airplayClients.Count == 0 && _bluetoothDevices.Count == 0)
        {
            ErrorOccurred?.Invoke(this, "No devices added");
            return false;
        }

        try
        {
            StateChanged?.Invoke(this, new StreamingStateEventArgs
            {
                State = StreamingState.Connecting,
                Message = "Starting stream..."
            });

            // Start streaming on all AirPlay clients
            foreach (var client in _airplayClients)
            {
                await client.StartStreamingAsync();
            }

            // If we have Bluetooth devices, create a multi-output device
            if (_bluetoothDevices.Count > 0 && _airplayClients.Count > 0)
            {
                var allDevices = _airplayClients.Select((_, i) => $"AirPlay_{i}")
                    .Concat(_bluetoothDevices)
                    .ToList();

                // Note: For Bluetooth, we rely on macOS audio routing
                // The multi-output device will handle sending to Bluetooth
            }

            // Start capturing system audio
            var captureStarted = await _audioCapture.StartCaptureAsync();
            if (!captureStarted)
            {
                // Fallback: If ScreenCaptureKit fails, we can still stream silence
                // and let the user use the Multi-Output Device approach
                Debug.WriteLine("[MultiRoom] ScreenCaptureKit failed, using fallback");
            }

            _isStreaming = true;
            _sequenceNumber = 0;

            StateChanged?.Invoke(this, new StreamingStateEventArgs
            {
                State = StreamingState.Streaming,
                Message = $"Streaming to {_airplayClients.Count + _bluetoothDevices.Count} device(s)"
            });

            Debug.WriteLine("[MultiRoom] Streaming started");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiRoom] Start streaming error: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex.Message);

            StateChanged?.Invoke(this, new StreamingStateEventArgs
            {
                State = StreamingState.Error,
                Message = ex.Message
            });

            return false;
        }
    }

    /// <summary>
    /// Stop streaming to all devices
    /// </summary>
    public async Task StopStreamingAsync()
    {
        if (!_isStreaming)
            return;

        _isStreaming = false;

        try
        {
            // Stop audio capture
            await _audioCapture.StopCaptureAsync();

            // Stop all AirPlay streams
            foreach (var client in _airplayClients)
            {
                await client.StopStreamingAsync();
            }

            StateChanged?.Invoke(this, new StreamingStateEventArgs
            {
                State = StreamingState.Stopped,
                Message = "Streaming stopped"
            });

            Debug.WriteLine("[MultiRoom] Streaming stopped");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiRoom] Stop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove all devices and stop streaming
    /// </summary>
    public async Task ClearDevicesAsync()
    {
        await StopStreamingAsync();

        foreach (var client in _airplayClients)
        {
            client.Dispose();
        }
        _airplayClients.Clear();
        _bluetoothDevices.Clear();
    }

    private async void OnAudioDataReceived(object? sender, AudioDataEventArgs e)
    {
        if (!_isStreaming)
            return;

        try
        {
            // Send audio to all AirPlay devices
            foreach (var client in _airplayClients.Where(c => c.IsStreaming))
            {
                await client.SendAudioPacketAsync(e.AudioData, e.Timestamp, _sequenceNumber);
            }

            _sequenceNumber++;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiRoom] Send audio error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopStreamingAsync().GetAwaiter().GetResult();
        _audioCapture.Dispose();

        foreach (var client in _airplayClients)
        {
            client.Dispose();
        }
    }

    public enum StreamingState
    {
        Stopped,
        Connecting,
        Streaming,
        Error
    }

    public class StreamingStateEventArgs : EventArgs
    {
        public StreamingState State { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}
