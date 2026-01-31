using System.Diagnostics;
using AirPlayStreamer.Services;

namespace AirPlayStreamer;

public partial class MainPage : ContentPage
{
    private readonly IAirPlayService _airPlayService;

    public MainPage(IAirPlayService airPlayService)
    {
        InitializeComponent();
        _airPlayService = airPlayService;

        // Hide Multi-Output section on iOS (only works on macOS)
#if IOS && !MACCATALYST
        MultiOutputSection.IsVisible = false;
#endif
    }

    private void OnAirPlayClicked(object sender, EventArgs e)
    {
        // Show native AirPlay picker
        _airPlayService.ShowAirPlayPicker();
        UpdateStatus("Select your HomePod from the AirPlay menu", Colors.Orange);
    }

    private async void OnBluetoothSettingsClicked(object sender, EventArgs e)
    {
        UpdateStatus("Opening Bluetooth settings...", Colors.Orange);

#if MACCATALYST
        // macOS - open System Settings > Bluetooth
        await RunCommandAsync("open", "x-apple.systempreferences:com.apple.BluetoothSettings");
#elif IOS
        // iOS - open Settings app (can't deep link to Bluetooth on iOS)
        if (await Launcher.TryOpenAsync("App-Prefs:Bluetooth"))
        {
            // Opened successfully
        }
        else
        {
            await DisplayAlert("Bluetooth",
                "Please open Settings > Bluetooth to connect your Echo Dot.\n\n" +
                "Say 'Alexa, pair' to put your Echo Dot in pairing mode.",
                "OK");
        }
#else
        await DisplayAlert("Bluetooth",
            "Please open your system's Bluetooth settings to connect your Echo Dot.",
            "OK");
#endif

        UpdateStatus("Connect Echo Dot via Bluetooth", Colors.Orange);
    }

    private async void OnMultiOutputClicked(object sender, EventArgs e)
    {
#if MACCATALYST
        UpdateStatus("Opening Audio MIDI Setup...", Colors.Orange);

        // Open Audio MIDI Setup
        await RunCommandAsync("open", "-a \"Audio MIDI Setup\"");

        await DisplayAlert("Create Multi-Output Device",
            "In Audio MIDI Setup:\n\n" +
            "1. Click '+' at bottom left\n" +
            "2. Select 'Create Multi-Output Device'\n" +
            "3. Check both your HomePod and Echo Dot\n" +
            "4. Right-click the new device → 'Use This Device For Sound Output'\n\n" +
            "Now all audio will play to both speakers!",
            "OK");

        UpdateStatus("Setup complete - play music!", Colors.LimeGreen);
#else
        await DisplayAlert("Not Available",
            "Multi-Output Device is only available on macOS.\n\n" +
            "On iOS, you can only output to one device at a time.",
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
