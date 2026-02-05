using AirPlayStreamer.Models;

namespace AirPlayStreamer.ViewModels;

public class DeviceViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceTypeDisplay { get; set; } = string.Empty;
    public string DeviceIcon { get; set; } = "📻";
    public Color StatusColor { get; set; } = Colors.Gray;
    public bool IsConnected { get; set; }
    public AirPlayDevice? OriginalDevice { get; set; }

    public static DeviceViewModel FromAirPlayDevice(AirPlayDevice device, bool isConnected = false)
    {
        return new DeviceViewModel
        {
            Id = device.Id,
            Name = string.IsNullOrEmpty(device.Name) ? "Unknown Device" : device.Name,
            DeviceTypeDisplay = GetDeviceTypeDisplay(device.DeviceType),
            DeviceIcon = GetDeviceIcon(device.DeviceType),
            StatusColor = isConnected ? Colors.LimeGreen : Colors.Gray,
            IsConnected = isConnected,
            OriginalDevice = device
        };
    }

    private static string GetDeviceTypeDisplay(AirPlayDeviceType type) => type switch
    {
        AirPlayDeviceType.HomePod => "HomePod",
        AirPlayDeviceType.HomePodMini => "HomePod mini",
        AirPlayDeviceType.AppleTV => "Apple TV",
        AirPlayDeviceType.AirPortExpress => "AirPort Express",
        AirPlayDeviceType.ThirdParty => "AirPlay Speaker",
        _ => "AirPlay Device"
    };

    private static string GetDeviceIcon(AirPlayDeviceType type) => type switch
    {
        AirPlayDeviceType.HomePod => "🔊",
        AirPlayDeviceType.HomePodMini => "🔊",
        AirPlayDeviceType.AppleTV => "📺",
        AirPlayDeviceType.AirPortExpress => "📡",
        _ => "🎵"
    };
}
