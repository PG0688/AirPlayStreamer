namespace AirPlayStreamer.Services.AirPlay;

/// <summary>
/// Service to capture system audio and route to multiple outputs
/// </summary>
public class AudioCaptureService : IDisposable
{
    private readonly List<RaopClient> _airplayClients = new();
    private CancellationTokenSource? _captureCts;
    private bool _isCapturing;

    public bool IsCapturing => _isCapturing;

    public event EventHandler<AudioCaptureEventArgs>? OnAudioCaptured;
    public event EventHandler<string>? OnError;

    /// <summary>
    /// Add an AirPlay device to stream to
    /// </summary>
    public async Task<bool> AddAirPlayDeviceAsync(string host, int port = 7000)
    {
        try
        {
            var client = new RaopClient(host, port);

            if (await client.ConnectAsync())
            {
                if (await client.SetupSessionAsync())
                {
                    _airplayClients.Add(client);
                    System.Diagnostics.Debug.WriteLine($"[AudioCapture] Added AirPlay device: {host}:{port}");
                    return true;
                }
            }

            client.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCapture] Error adding device: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Remove an AirPlay device
    /// </summary>
    public async Task RemoveAirPlayDeviceAsync(string host)
    {
        var client = _airplayClients.FirstOrDefault(c => c.ToString()?.Contains(host) == true);
        if (client != null)
        {
            await client.StopStreamingAsync();
            client.Dispose();
            _airplayClients.Remove(client);
        }
    }

    /// <summary>
    /// Start capturing and streaming audio
    /// </summary>
    public async Task StartCaptureAsync()
    {
        if (_isCapturing)
            return;

        _captureCts = new CancellationTokenSource();
        _isCapturing = true;

        // Start streaming on all connected devices
        foreach (var client in _airplayClients)
        {
            await client.StartStreamingAsync(_captureCts.Token);
        }

        // Start the audio capture loop
        _ = CaptureLoopAsync(_captureCts.Token);

        System.Diagnostics.Debug.WriteLine("[AudioCapture] Started capturing");
    }

    /// <summary>
    /// Stop capturing and streaming
    /// </summary>
    public async Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureCts?.Cancel();

        foreach (var client in _airplayClients)
        {
            await client.StopStreamingAsync();
        }

        System.Diagnostics.Debug.WriteLine("[AudioCapture] Stopped capturing");
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        ushort sequenceNumber = 0;
        uint timestamp = 0;
        const int sampleRate = 44100;
        const int samplesPerPacket = 352; // Standard ALAC frame size

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // In a real implementation, this would capture audio from:
                // - System audio (using ScreenCaptureKit or similar)
                // - Audio file
                // - Microphone
                // For now, we'll generate silence as a placeholder

                var audioData = new byte[samplesPerPacket * 4]; // 16-bit stereo

                // Notify listeners (could be used to visualize audio)
                OnAudioCaptured?.Invoke(this, new AudioCaptureEventArgs
                {
                    AudioData = audioData,
                    Timestamp = timestamp,
                    SampleRate = sampleRate
                });

                // Send to all connected AirPlay devices
                foreach (var client in _airplayClients.Where(c => c.IsStreaming))
                {
                    await client.SendAudioPacketAsync(audioData, timestamp, sequenceNumber);
                }

                sequenceNumber++;
                timestamp += samplesPerPacket;

                // Wait for next packet time (352 samples at 44100 Hz = ~8ms)
                await Task.Delay(8, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCapture] Capture loop error: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    public void Dispose()
    {
        _captureCts?.Cancel();
        _captureCts?.Dispose();

        foreach (var client in _airplayClients)
        {
            client.Dispose();
        }
        _airplayClients.Clear();
    }
}

public class AudioCaptureEventArgs : EventArgs
{
    public byte[] AudioData { get; init; } = Array.Empty<byte>();
    public uint Timestamp { get; init; }
    public int SampleRate { get; init; }
}
