// Models/WifiNetworkItem.cs
namespace HSED_2._0.Models;

public sealed class WifiNetworkItem
{
    public string Ssid { get; set; } = "";
    public string Security { get; set; } = "";
    public int? SignalDbm { get; set; }
    public int? FreqMhz { get; set; }

    public string SignalText => SignalDbm is null ? "" : $"{SignalDbm} dBm";
    public string Details => $"{(FreqMhz is null ? "" : $"{FreqMhz} MHz")}".Trim();
}
