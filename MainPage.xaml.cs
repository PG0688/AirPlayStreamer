using System.Collections.ObjectModel;
using System.Diagnostics;
using AirPlayStreamer.Models;
using AirPlayStreamer.Services;
using AirPlayStreamer.ViewModels;
#if MACCATALYST
using AirPlayStreamer.Platforms.MacCatalyst.Services;
#endif

namespace AirPlayStreamer;

public partial class MainPage : ContentPage
{
    private readonly IAirPlayService _airPlayService;
#if MACCATALYST
    private readonly CoreAudioService? _coreAudioService;
#endif
    private CancellationTokenSource? _discoveryCts;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

#if MACCATALYST
    public MainPage(IAirPlayService airPlayService, CoreAudioService coreAudioService)
    {
        InitializeComponent();
        _airPlayService = airPlayService;
        _coreAudioService = coreAudioService;
        BindingContext = this;
#else
    public MainPage(IAirPlayService airPlayService)
    {
        InitializeComponent();
        _airPlayService = airPlayService;
        BindingContext = this;
#endif

        // Subscribe to device changes
        _airPlayService.DevicesChanged += OnDevicesChanged;
        _airPlayService.ConnectionStateChanged += OnConnectionStateChanged;

        // Hide Multi-Output section on iOS (only works on macOS)
#if IOS && !MACCATALYST
        MultiOutputSection.IsVisible = false;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Auto-start discovery when page appears
        await StartDiscoveryAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopDiscovery();
    }

    private async void OnScanClicked(object sender, EventArgs e)
    {
        await StartDiscoveryAsync();
    }

    private async Task StartDiscoveryAsync()
    {
        try
        {
            // Cancel any existing discovery
            StopDiscovery();

            // Update UI
            ScanButton.IsEnabled = false;
            ScanButton.Text = "Scanning...";
            ScanningIndicator.IsVisible = true;
            UpdateStatus("Scanning for devices...", Colors.Orange);

            _discoveryCts = new CancellationTokenSource();

            // Start discovery
            await _airPlayService.StartDiscoveryAsync(_discoveryCts.Token);

            // Give it some time to find devices
            await Task.Delay(3000, _discoveryCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discovery error: {ex.Message}");
            UpdateStatus($"Discovery failed: {ex.Message}", Colors.Red);
        }
        finally
        {
            // Update UI
            ScanButton.IsEnabled = true;
            ScanButton.Text = "Scan";
            ScanningIndicator.IsVisible = false;

            if (Devices.Count == 0)
            {
                UpdateStatus("No devices found", Colors.Gray);
            }
            else
            {
                UpdateStatus($"Found {Devices.Count} device(s)", Colors.LimeGreen);
            }
        }
    }

    private void StopDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = null;
        _airPlayService.StopDiscovery();
    }

    private void OnDevicesChanged(object? sender, AirPlayDevicesChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update the devices collection
            Devices.Clear();
            foreach (var device in e.Devices)
            {
                Devices.Add(DeviceViewModel.FromAirPlayDevice(device));
            }

            // Update UI visibility
            var hasDevices = Devices.Count > 0;
            DevicesList.IsVisible = hasDevices;
            NoDevicesLabel.IsVisible = !hasDevices;

            if (hasDevices)
            {
                UpdateStatus($"Found {Devices.Count} device(s)", Colors.LimeGreen);
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, AirPlayConnectionEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var statusMessage = e.State switch
            {
                AirPlayConnectionState.Connecting => $"Connecting to {e.Device?.Name}...",
                AirPlayConnectionState.Connected => $"Connected to {e.Device?.Name}",
                AirPlayConnectionState.Disconnected => "Disconnected",
                AirPlayConnectionState.Failed => $"Connection failed: {e.ErrorMessage}",
                _ => "Unknown state"
            };

            var statusColor = e.State switch
            {
                AirPlayConnectionState.Connecting => Colors.Orange,
                AirPlayConnectionState.Connected => Colors.LimeGreen,
                AirPlayConnectionState.Disconnected => Colors.Gray,
                AirPlayConnectionState.Failed => Colors.Red,
                _ => Colors.Gray
            };

            UpdateStatus(statusMessage, statusColor);

            // Update device status in list
            if (e.Device != null)
            {
                var deviceVm = Devices.FirstOrDefault(d => d.Id == e.Device.Id);
                if (deviceVm != null)
                {
                    var index = Devices.IndexOf(deviceVm);
                    Devices[index] = DeviceViewModel.FromAirPlayDevice(e.Device, e.State == AirPlayConnectionState.Connected);
                }
            }
        });
    }

    private async void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not DeviceViewModel selectedDevice)
            return;

        // Clear selection
        DevicesList.SelectedItem = null;

        if (selectedDevice.OriginalDevice == null)
            return;

        // Try to connect
        UpdateStatus($"Connecting to {selectedDevice.Name}...", Colors.Orange);

        try
        {
            var success = await _airPlayService.ConnectAsync(selectedDevice.OriginalDevice);
            if (success)
            {
                UpdateStatus($"Connected to {selectedDevice.Name}", Colors.LimeGreen);
            }
            else
            {
                UpdateStatus($"Failed to connect to {selectedDevice.Name}", Colors.Red);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Connection error: {ex.Message}", Colors.Red);
        }
    }

    private void OnAirPlayClicked(object sender, EventArgs e)
    {
        _airPlayService.ShowAirPlayPicker();
        UpdateStatus("Select device from AirPlay menu", Colors.Orange);
    }

    private async void OnBluetoothSettingsClicked(object sender, EventArgs e)
    {
        UpdateStatus("Opening Bluetooth settings...", Colors.Orange);

#if MACCATALYST
        await RunCommandAsync("open", "x-apple.systempreferences:com.apple.BluetoothSettings");
#elif IOS
        if (await Launcher.TryOpenAsync("App-Prefs:Bluetooth"))
        {
            // Opened successfully
        }
        else
        {
            await DisplayAlert("Bluetooth",
                "Please open Settings > Bluetooth to connect your Echo Dot.\n\n" +
                "Say 'Alexa, pair' to put it in pairing mode.",
                "OK");
        }
#else
        await DisplayAlert("Bluetooth",
            "Please open your system's Bluetooth settings.",
            "OK");
#endif

        UpdateStatus("Connect Echo Dot via Bluetooth", Colors.Orange);
    }

    private async void OnMultiOutputClicked(object sender, EventArgs e)
    {
#if MACCATALYST
        if (_coreAudioService == null)
        {
            await DisplayAlert("Error", "Audio service not available.", "OK");
            return;
        }

        UpdateStatus("Creating Multi-Output Device...", Colors.Orange);
        MultiOutputButton.IsEnabled = false;

        try
        {
            // Get available audio devices
            var audioDevices = await _coreAudioService.GetOutputDevicesAsync();
            var bluetoothDevices = await _coreAudioService.GetBluetoothAudioDevicesAsync();

            // Find connected devices to include
            var devicesToInclude = new List<string>();

            // Add any connected Bluetooth audio devices (like Echo Dot)
            foreach (var bt in bluetoothDevices.Where(d => d.IsConnected))
            {
                devicesToInclude.Add(bt.Name);
                Debug.WriteLine($"[MultiOutput] Including Bluetooth: {bt.Name}");
            }

            // Add AirPlay devices if discovered
            foreach (var airplay in _airPlayService.DiscoveredDevices)
            {
                devicesToInclude.Add(airplay.Name);
                Debug.WriteLine($"[MultiOutput] Including AirPlay: {airplay.Name}");
            }

            if (devicesToInclude.Count < 2)
            {
                // Not enough devices - show device selection
                var deviceNames = audioDevices.Select(d => d.Name).ToList();
                deviceNames.AddRange(bluetoothDevices.Where(d => d.IsPaired).Select(d => d.Name));

                if (deviceNames.Count < 2)
                {
                    await DisplayAlert("Not Enough Devices",
                        "Connect at least 2 audio devices:\n" +
                        "• HomePod via AirPlay (use the picker above)\n" +
                        "• Echo Dot via Bluetooth\n\n" +
                        "Then try again.",
                        "OK");
                    UpdateStatus("Connect more devices first", Colors.Orange);
                    return;
                }

                // Let user select devices manually
                var selected = await DisplayActionSheet(
                    "Select first device:",
                    "Cancel", null,
                    deviceNames.ToArray());

                if (string.IsNullOrEmpty(selected) || selected == "Cancel")
                {
                    UpdateStatus("Cancelled", Colors.Gray);
                    return;
                }

                devicesToInclude.Add(selected);
                deviceNames.Remove(selected);

                var selected2 = await DisplayActionSheet(
                    "Select second device:",
                    "Cancel", null,
                    deviceNames.ToArray());

                if (string.IsNullOrEmpty(selected2) || selected2 == "Cancel")
                {
                    UpdateStatus("Cancelled", Colors.Gray);
                    return;
                }

                devicesToInclude.Add(selected2);
            }

            // Create the Multi-Output Device
            UpdateStatus($"Creating Multi-Output with {devicesToInclude.Count} devices...", Colors.Orange);

            var success = await _coreAudioService.CreateMultiOutputDeviceAsync(
                "Multi-Room Audio",
                devicesToInclude);

            if (success)
            {
                UpdateStatus("Multi-Output Device ready!", Colors.LimeGreen);
                await DisplayAlert("Success!",
                    $"Created 'Multi-Room Audio' combining:\n" +
                    string.Join("\n", devicesToInclude.Select(d => $"• {d}")) +
                    "\n\nAll audio will now play to both speakers!",
                    "OK");
            }
            else
            {
                // Fallback: Open Audio MIDI Setup with instructions
                UpdateStatus("Manual setup required", Colors.Orange);
                await DisplayAlert("Manual Setup Required",
                    "Audio MIDI Setup will open.\n\n" +
                    "1. Click '+' → 'Create Multi-Output Device'\n" +
                    "2. Check your HomePod and Echo Dot\n" +
                    "3. Right-click → 'Use This Device For Sound Output'",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiOutput] Error: {ex.Message}");
            UpdateStatus($"Error: {ex.Message}", Colors.Red);
        }
        finally
        {
            MultiOutputButton.IsEnabled = true;
        }
#else
        await DisplayAlert("Not Available",
            "Multi-Output Device is only available on macOS.",
            "OK");
#endif
    }

    private async void OnYouTubeMusicClicked(object sender, EventArgs e)
    {
#if MACCATALYST
        await RunCommandAsync("open", "https://music.youtube.com");
#else
        await Launcher.TryOpenAsync("https://music.youtube.com");
#endif
        UpdateStatus("Enjoy your music!", Colors.LimeGreen);
    }

    private async void OnSystemAudioClicked(object sender, EventArgs e)
    {
#if MACCATALYST
        await RunCommandAsync("open", "x-apple.systempreferences:com.apple.Sound-Settings.extension");
        UpdateStatus("Configure system audio output", Colors.Orange);
#else
        await DisplayAlert("System Audio",
            "Open your device's sound settings to configure audio output.",
            "OK");
#endif
    }

    private void UpdateStatus(string message, Color color)
    {
        StatusLabel.Text = message;
        StatusIcon.TextColor = color;
    }

#if MACCATALYST
    private static async Task RunCommandAsync(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Command error: {ex.Message}");
        }
    }
#endif
}
