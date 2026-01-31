using System.Runtime.InteropServices;
using AVFoundation;
using CoreMedia;
using Foundation;
using ObjCRuntime;

namespace AirPlayStreamer.Platforms.MacCatalyst.Services;

/// <summary>
/// Audio capture service for macOS
/// Note: ScreenCaptureKit requires special permissions and is complex to implement.
/// This implementation provides a fallback that generates test audio or silence,
/// while the Multi-Output Device approach handles actual audio routing through macOS.
/// </summary>
public class ScreenCaptureAudioService : IDisposable
{
    private CancellationTokenSource? _captureCts;
    private bool _isCapturing;
    private bool _generateTestTone;

    public event EventHandler<AudioDataEventArgs>? AudioDataReceived;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// Start audio capture (or test tone generation as fallback)
    /// </summary>
    public async Task<bool> StartCaptureAsync()
    {
        if (_isCapturing)
            return true;

        try
        {
            // Try to use ScreenCaptureKit first (requires macOS 13+ and permissions)
            var screenCaptureAvailable = await TryStartScreenCaptureAsync();

            if (!screenCaptureAvailable)
            {
                // Fallback: Generate audio packets for the streaming protocol
                // The actual audio routing happens via macOS Multi-Output Device
                System.Diagnostics.Debug.WriteLine("[AudioCapture] Using fallback mode - Multi-Output Device handles audio routing");
                _generateTestTone = false; // Set to true for test tone
            }

            _isCapturing = true;
            _captureCts = new CancellationTokenSource();

            // Start the audio generation loop
            _ = AudioLoopAsync(_captureCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCapture] Start error: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
    }

    private async Task<bool> TryStartScreenCaptureAsync()
    {
        try
        {
            // Check if we're on macOS 13+ and have permissions
            // For now, return false to use the fallback
            // Full ScreenCaptureKit implementation would go here
            await Task.Delay(1);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task AudioLoopAsync(CancellationToken cancellationToken)
    {
        const int sampleRate = 44100;
        const int channels = 2;
        const int samplesPerPacket = 352; // Standard ALAC frame size
        const int bytesPerSample = 2; // 16-bit
        const int packetSize = samplesPerPacket * channels * bytesPerSample;

        ushort sequenceNumber = 0;
        uint timestamp = 0;
        double phase = 0;
        const double frequency = 440; // A4 note for test tone
        const double amplitude = 0.3;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var audioData = new byte[packetSize];

                if (_generateTestTone)
                {
                    // Generate a sine wave test tone
                    for (int i = 0; i < samplesPerPacket; i++)
                    {
                        var sample = (short)(amplitude * short.MaxValue * Math.Sin(phase));
                        phase += 2 * Math.PI * frequency / sampleRate;
                        if (phase > 2 * Math.PI) phase -= 2 * Math.PI;

                        // Left channel
                        audioData[i * 4] = (byte)(sample & 0xFF);
                        audioData[i * 4 + 1] = (byte)((sample >> 8) & 0xFF);
                        // Right channel
                        audioData[i * 4 + 2] = (byte)(sample & 0xFF);
                        audioData[i * 4 + 3] = (byte)((sample >> 8) & 0xFF);
                    }
                }
                // else: audioData is already zeroed (silence)

                AudioDataReceived?.Invoke(this, new AudioDataEventArgs
                {
                    AudioData = audioData,
                    Timestamp = timestamp,
                    SampleRate = sampleRate,
                    Channels = channels
                });

                sequenceNumber++;
                timestamp += samplesPerPacket;

                // Wait for next packet time (352 samples at 44100 Hz ≈ 8ms)
                await Task.Delay(8, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioCapture] Loop error: {ex.Message}");
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Stop audio capture
    /// </summary>
    public Task StopCaptureAsync()
    {
        _isCapturing = false;
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;

        System.Diagnostics.Debug.WriteLine("[AudioCapture] Stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopCaptureAsync().GetAwaiter().GetResult();
    }
}

public class AudioDataEventArgs : EventArgs
{
    public byte[] AudioData { get; init; } = Array.Empty<byte>();
    public uint Timestamp { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
}
