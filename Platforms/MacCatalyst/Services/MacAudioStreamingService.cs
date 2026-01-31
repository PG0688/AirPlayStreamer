using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AirPlayStreamer.Services;

namespace AirPlayStreamer.Platforms.MacCatalyst.Services;

public partial class MacAudioStreamingService : IAudioStreamingService
{
    private StreamingState _state = StreamingState.Stopped;
    private readonly List<string> _activeDevices = new();

    public StreamingState State => _state;
    public event EventHandler<StreamingStateChangedEventArgs>? StateChanged;

    public async Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync()
    {
        var devices = new List<AudioOutputDevice>();

        try
        {
            // Use system_profiler to get audio devices
            var output = await RunCommandAsync("system_profiler", "SPAudioDataType -json");

            if (!string.IsNullOrEmpty(output))
            {
                var json = JsonDocument.Parse(output);
                var audioData = json.RootElement.GetProperty("SPAudioDataType");

                foreach (var item in audioData.EnumerateArray())
                {
                    if (item.TryGetProperty("_items", out var items))
                    {
                        foreach (var device in items.EnumerateArray())
                        {
                            var name = device.TryGetProperty("_name", out var n) ? n.GetString() : "Unknown";
                            var coreaudioType = device.TryGetProperty("coreaudio_device_transport", out var t) ? t.GetString() : "";

                            devices.Add(new AudioOutputDevice
                            {
                                Id = name ?? "Unknown",
                                Name = name ?? "Unknown",
                                Type = ParseDeviceType(coreaudioType ?? ""),
                                IsDefault = device.TryGetProperty("coreaudio_default_audio_output_device", out var def) &&
                                           def.GetString() == "spaudio_yes"
                            });
                        }
                    }
                }
            }

            // Also try to get AirPlay devices from the discovered list
            // These would need to be added separately when streaming
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Error getting output devices: {ex.Message}");
        }

        return devices;
    }

    public async Task<IReadOnlyList<BluetoothAudioDevice>> GetBluetoothDevicesAsync()
    {
        var devices = new List<BluetoothAudioDevice>();

        try
        {
            // Use system_profiler to get Bluetooth devices
            var output = await RunCommandAsync("system_profiler", "SPBluetoothDataType -json");

            if (!string.IsNullOrEmpty(output))
            {
                var json = JsonDocument.Parse(output);
                var btData = json.RootElement.GetProperty("SPBluetoothDataType");

                foreach (var controller in btData.EnumerateArray())
                {
                    if (controller.TryGetProperty("device_connected", out var connected))
                    {
                        foreach (var device in connected.EnumerateObject())
                        {
                            var props = device.Value;
                            var name = device.Name;
                            var address = props.TryGetProperty("device_address", out var addr) ? addr.GetString() : "";
                            var isAudio = props.TryGetProperty("device_minorType", out var minor) &&
                                         (minor.GetString()?.Contains("Audio") ?? false);

                            if (isAudio || props.TryGetProperty("device_services", out var services))
                            {
                                devices.Add(new BluetoothAudioDevice
                                {
                                    Address = address ?? "",
                                    Name = name,
                                    IsConnected = true,
                                    IsPaired = true
                                });
                            }
                        }
                    }

                    if (controller.TryGetProperty("device_not_connected", out var notConnected))
                    {
                        foreach (var device in notConnected.EnumerateObject())
                        {
                            var props = device.Value;
                            var name = device.Name;
                            var address = props.TryGetProperty("device_address", out var addr) ? addr.GetString() : "";

                            devices.Add(new BluetoothAudioDevice
                            {
                                Address = address ?? "",
                                Name = name,
                                IsConnected = false,
                                IsPaired = true
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Error getting Bluetooth devices: {ex.Message}");
        }

        return devices;
    }

    public async Task<bool> CreateMultiOutputDeviceAsync(string name, IEnumerable<string> deviceIds)
    {
        try
        {
            var deviceList = deviceIds.ToList();
            if (deviceList.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine("[Audio] Need at least 2 devices for multi-output");
                return false;
            }

            // Use AppleScript to create multi-output device via Audio MIDI Setup
            // This is a workaround since CoreAudio APIs aren't directly available
            var script = $@"
tell application ""Audio MIDI Setup""
    activate
end tell

delay 1

tell application ""System Events""
    tell process ""Audio MIDI Setup""
        -- Click the + button and create Multi-Output Device
        click button 1 of group 1 of window 1
        delay 0.5
        click menu item ""Create Multi-Output Device"" of menu 1 of button 1 of group 1 of window 1
    end tell
end tell
";
            // For now, just open Audio MIDI Setup and let the user configure
            // A full implementation would use CoreAudio C APIs via P/Invoke
            await RunCommandAsync("open", "-a \"Audio MIDI Setup\"");

            System.Diagnostics.Debug.WriteLine("[Audio] Opened Audio MIDI Setup - manual configuration required");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Error creating multi-output: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetSystemOutputAsync(string deviceId)
    {
        try
        {
            // Try using SwitchAudioSource if installed
            var result = await RunCommandAsync("which", "SwitchAudioSource");
            if (!string.IsNullOrEmpty(result))
            {
                await RunCommandAsync("SwitchAudioSource", $"-s \"{deviceId}\"");
                return true;
            }

            // Fallback to AppleScript
            var script = $@"
set deviceName to ""{deviceId}""
tell application ""System Preferences""
    reveal anchor ""output"" of pane id ""com.apple.preference.sound""
end tell

delay 0.5

tell application ""System Events""
    tell process ""System Preferences""
        select row 1 of table 1 of scroll area 1 of tab group 1 of window 1 whose value of text field 1 is deviceName
    end tell
end tell
";
            await RunCommandAsync("osascript", $"-e '{script}'");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Error setting output: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartAirPlayStreamAsync(string deviceIp, int port)
    {
        UpdateState(StreamingState.Connecting);

        try
        {
            // TODO: Implement RAOP protocol for direct AirPlay streaming
            // For now, we'll use the system AirPlay
            System.Diagnostics.Debug.WriteLine($"[Audio] Starting AirPlay stream to {deviceIp}:{port}");

            // This would require implementing:
            // 1. RTSP session setup (ANNOUNCE, SETUP, RECORD)
            // 2. RTP audio streaming
            // 3. ALAC or AAC encoding

            _activeDevices.Add($"{deviceIp}:{port}");
            UpdateState(StreamingState.Streaming);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] AirPlay error: {ex.Message}");
            UpdateState(StreamingState.Error, ex.Message);
            return false;
        }
    }

    public Task StopAirPlayStreamAsync()
    {
        _activeDevices.Clear();
        UpdateState(StreamingState.Stopped);
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectBluetoothAsync(string deviceAddress)
    {
        try
        {
            // Use blueutil if installed, or AppleScript
            var result = await RunCommandAsync("which", "blueutil");
            if (!string.IsNullOrEmpty(result))
            {
                await RunCommandAsync("blueutil", $"--connect {deviceAddress}");
                _activeDevices.Add(deviceAddress);
                return true;
            }

            // Fallback - open Bluetooth preferences
            await RunCommandAsync("open", "x-apple.systempreferences:com.apple.BluetoothSettings");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Bluetooth error: {ex.Message}");
            return false;
        }
    }

    private void UpdateState(StreamingState state, string? error = null)
    {
        _state = state;
        StateChanged?.Invoke(this, new StreamingStateChangedEventArgs
        {
            State = state,
            ErrorMessage = error,
            ActiveDevices = _activeDevices.AsReadOnly()
        });
    }

    private static AudioDeviceType ParseDeviceType(string transport)
    {
        return transport.ToLowerInvariant() switch
        {
            "coreaudio_device_type_builtin" => AudioDeviceType.BuiltIn,
            var t when t.Contains("airplay") => AudioDeviceType.AirPlay,
            var t when t.Contains("bluetooth") => AudioDeviceType.Bluetooth,
            var t when t.Contains("usb") => AudioDeviceType.USB,
            var t when t.Contains("aggregate") => AudioDeviceType.Aggregate,
            _ => AudioDeviceType.Other
        };
    }

    private static async Task<string> RunCommandAsync(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Command error ({command}): {ex.Message}");
            return string.Empty;
        }
    }
}
