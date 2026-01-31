namespace AirPlayStreamer.Models;

public class AirPlayDevice : IEquatable<AirPlayDevice>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 7000;
    public string Model { get; set; } = string.Empty;
    public AirPlayDeviceType DeviceType { get; set; } = AirPlayDeviceType.Unknown;
    public bool SupportsAudio { get; set; } = true;
    public bool SupportsVideo { get; set; }
    public Dictionary<string, string> TxtRecords { get; set; } = new();

    public bool Equals(AirPlayDevice? other)
    {
        if (other is null) return false;
        return Id == other.Id || (IPAddress == other.IPAddress && Port == other.Port);
    }

    public override bool Equals(object? obj) => Equals(obj as AirPlayDevice);

    public override int GetHashCode() => Id.GetHashCode();
}

public enum AirPlayDeviceType
{
    Unknown,
    AppleTV,
    HomePod,
    HomePodMini,
    AirPortExpress,
    ThirdParty
}
