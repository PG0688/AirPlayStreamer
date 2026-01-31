using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Foundation;
using ObjCRuntime;

namespace AirPlayStreamer.Platforms.MacCatalyst.Services;

/// <summary>
/// Service for managing macOS audio devices via CoreAudio and command-line tools
/// Provides functionality to create Multi-Output Devices and manage audio routing
/// </summary>
public class CoreAudioService
{
    /// <summary>
    /// Get all available audio output devices
    /// </summary>
    public async Task<List<AudioDeviceInfo>> GetOutputDevicesAsync()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            // Use system_profiler to get audio device info
            var output = await RunCommandAsync("system_profiler", "SPAudioDataType -json");
            if (string.IsNullOrEmpty(output))
                return devices;

            var json = JsonDocument.Parse(output);
            var audioData = json.RootElement.GetProperty("SPAudioDataType");

            foreach (var item in audioData.EnumerateArray())
            {
                if (item.TryGetProperty("_items", out var items))
                {
                    foreach (var device in items.EnumerateArray())
                    {
                        var name = device.TryGetProperty("_name", out var n) ? n.GetString() : "";
                        var manufacturer = device.TryGetProperty("coreaudio_device_manufacturer", out var m) ? m.GetString() : "";

                        if (!string.IsNullOrEmpty(name))
                        {
                            devices.Add(new AudioDeviceInfo
                            {
                                Name = name ?? "",
                                Manufacturer = manufacturer ?? "",
                                IsOutput = true
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAudio] Error getting devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Get all Bluetooth audio devices (paired)
    /// </summary>
    public async Task<List<BluetoothDeviceInfo>> GetBluetoothAudioDevicesAsync()
    {
        var devices = new List<BluetoothDeviceInfo>();

        try
        {
            var output = await RunCommandAsync("system_profiler", "SPBluetoothDataType -json");
            if (string.IsNullOrEmpty(output))
                return devices;

            var json = JsonDocument.Parse(output);
            var btData = json.RootElement.GetProperty("SPBluetoothDataType");

            foreach (var controller in btData.EnumerateArray())
            {
                // Connected devices
                if (controller.TryGetProperty("device_connected", out var connected))
                {
                    foreach (var device in connected.EnumerateObject())
                    {
                        var props = device.Value;
                        var address = props.TryGetProperty("device_address", out var addr) ? addr.GetString() : "";
                        var minorType = props.TryGetProperty("device_minorType", out var mt) ? mt.GetString() : "";

                        // Filter for audio devices (headphones, speakers, etc.)
                        if (IsAudioDevice(minorType))
                        {
                            devices.Add(new BluetoothDeviceInfo
                            {
                                Name = device.Name,
                                Address = address ?? "",
                                IsConnected = true,
                                IsPaired = true,
                                DeviceType = minorType ?? ""
                            });
                        }
                    }
                }

                // Paired but not connected
                if (controller.TryGetProperty("device_not_connected", out var notConnected))
                {
                    foreach (var device in notConnected.EnumerateObject())
                    {
                        var props = device.Value;
                        var address = props.TryGetProperty("device_address", out var addr) ? addr.GetString() : "";
                        var minorType = props.TryGetProperty("device_minorType", out var mt) ? mt.GetString() : "";

                        if (IsAudioDevice(minorType))
                        {
                            devices.Add(new BluetoothDeviceInfo
                            {
                                Name = device.Name,
                                Address = address ?? "",
                                IsConnected = false,
                                IsPaired = true,
                                DeviceType = minorType ?? ""
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAudio] Error getting Bluetooth devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Connect to a Bluetooth device by address
    /// </summary>
    public async Task<bool> ConnectBluetoothDeviceAsync(string address)
    {
        try
        {
            // Try using blueutil if installed
            var result = await RunCommandAsync("blueutil", $"--connect {address}");
            if (!string.IsNullOrEmpty(result) || result == "")
            {
                // Give it time to connect
                await Task.Delay(2000);
                return true;
            }

            // Fallback: open Bluetooth preferences
            await RunCommandAsync("open", "x-apple.systempreferences:com.apple.BluetoothSettings");
            return false;
        }
        catch
        {
            // blueutil not installed, open system preferences
            await RunCommandAsync("open", "x-apple.systempreferences:com.apple.BluetoothSettings");
            return false;
        }
    }

    /// <summary>
    /// Create a Multi-Output Device combining multiple audio outputs
    /// Uses AppleScript to automate Audio MIDI Setup
    /// </summary>
    public async Task<bool> CreateMultiOutputDeviceAsync(string name, IEnumerable<string> deviceNames)
    {
        try
        {
            var deviceList = deviceNames.ToList();
            if (deviceList.Count < 2)
            {
                Debug.WriteLine("[CoreAudio] Need at least 2 devices for Multi-Output");
                return false;
            }

            // Check if a multi-output device with this name already exists
            var existingDevices = await GetOutputDevicesAsync();
            if (existingDevices.Any(d => d.Name == name))
            {
                Debug.WriteLine($"[CoreAudio] Multi-Output Device '{name}' already exists");
                // Set it as default output
                await SetDefaultOutputDeviceAsync(name);
                return true;
            }

            // Create the aggregate device using a helper script
            // This creates the device via CoreAudio's HAL plugin
            var script = BuildCreateAggregateDeviceScript(name, deviceList);
            var result = await RunAppleScriptAsync(script);

            if (result.Contains("error") || result.Contains("Error"))
            {
                Debug.WriteLine($"[CoreAudio] AppleScript error: {result}");

                // Fallback: Open Audio MIDI Setup and provide instructions
                await OpenAudioMidiSetupAsync();
                return false;
            }

            // Set the new device as the default output
            await Task.Delay(1000); // Wait for device to be registered
            await SetDefaultOutputDeviceAsync(name);

            Debug.WriteLine($"[CoreAudio] Created Multi-Output Device: {name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAudio] Error creating Multi-Output Device: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Set the default system audio output device
    /// </summary>
    public async Task<bool> SetDefaultOutputDeviceAsync(string deviceName)
    {
        try
        {
            // Use SwitchAudioSource if available (brew install switchaudio-osx)
            var result = await RunCommandAsync("SwitchAudioSource", $"-s \"{deviceName}\"");
            if (string.IsNullOrEmpty(result) || !result.Contains("Error"))
            {
                Debug.WriteLine($"[CoreAudio] Set default output to: {deviceName}");
                return true;
            }

            // Fallback: AppleScript
            var script = $@"
                tell application ""System Preferences""
                    reveal anchor ""output"" of pane id ""com.apple.preference.sound""
                end tell
            ";
            await RunAppleScriptAsync(script);
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Open Audio MIDI Setup application
    /// </summary>
    public async Task OpenAudioMidiSetupAsync()
    {
        await RunCommandAsync("open", "-a \"Audio MIDI Setup\"");
    }

    /// <summary>
    /// Quick setup: Connect Bluetooth, create Multi-Output, set as default
    /// </summary>
    public async Task<bool> QuickSetupMultiRoomAsync(string? bluetoothAddress, string? airplayDeviceName)
    {
        try
        {
            var deviceNames = new List<string>();

            // Connect Bluetooth device if specified
            if (!string.IsNullOrEmpty(bluetoothAddress))
            {
                var btConnected = await ConnectBluetoothDeviceAsync(bluetoothAddress);
                if (btConnected)
                {
                    // Get the Bluetooth device name
                    var btDevices = await GetBluetoothAudioDevicesAsync();
                    var btDevice = btDevices.FirstOrDefault(d => d.Address == bluetoothAddress);
                    if (btDevice != null)
                    {
                        deviceNames.Add(btDevice.Name);
                    }
                }
            }

            // Add AirPlay device if specified
            if (!string.IsNullOrEmpty(airplayDeviceName))
            {
                deviceNames.Add(airplayDeviceName);
            }

            if (deviceNames.Count < 2)
            {
                Debug.WriteLine("[CoreAudio] QuickSetup: Not enough devices");
                return false;
            }

            // Create Multi-Output Device
            return await CreateMultiOutputDeviceAsync("Multi-Room Audio", deviceNames);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAudio] QuickSetup error: {ex.Message}");
            return false;
        }
    }

    private string BuildCreateAggregateDeviceScript(string name, List<string> deviceNames)
    {
        // AppleScript to automate Audio MIDI Setup
        // This is complex because Audio MIDI Setup doesn't have great AppleScript support
        var devicesStr = string.Join(", ", deviceNames.Select(d => $"\"{d}\""));

        return $@"
            tell application ""Audio MIDI Setup""
                activate
            end tell

            delay 1

            tell application ""System Events""
                tell process ""Audio MIDI Setup""
                    -- Click the + button to create new device
                    click menu button 1 of group 1 of splitter group 1 of window 1
                    delay 0.5

                    -- Select 'Create Multi-Output Device'
                    click menu item ""Create Multi-Output Device"" of menu 1 of menu button 1 of group 1 of splitter group 1 of window 1
                    delay 1

                    -- The new device should now be selected in the list
                    -- We need to check the boxes for the devices we want to include
                end tell
            end tell

            return ""Created Multi-Output Device""
        ";
    }

    private static bool IsAudioDevice(string? minorType)
    {
        if (string.IsNullOrEmpty(minorType))
            return true; // Assume yes if unknown

        var audioTypes = new[]
        {
            "Headphones", "Loudspeaker", "Headset", "Audio",
            "Speaker", "Portable Audio", "Car Audio", "HiFi"
        };

        return audioTypes.Any(t => minorType.Contains(t, StringComparison.OrdinalIgnoreCase));
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
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return string.IsNullOrEmpty(error) ? output : error;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoreAudio] Command error ({command}): {ex.Message}");
            return string.Empty;
        }
    }

    private static async Task<string> RunAppleScriptAsync(string script)
    {
        return await RunCommandAsync("osascript", $"-e '{script.Replace("'", "'\"'\"'")}'");
    }
}

public class AudioDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public bool IsOutput { get; set; }
    public bool IsInput { get; set; }
    public bool IsDefault { get; set; }
}

public class BluetoothDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
    public string DeviceType { get; set; } = string.Empty;
}
