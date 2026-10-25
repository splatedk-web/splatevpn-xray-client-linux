using System.Net.NetworkInformation;
namespace SplateClientDesktop.Services;
public record NetworkProfileContext(string Key,string Label)
{
    public static async Task<NetworkProfileContext> CaptureAsync(CancellationToken ct)
    {
        var uplink=await TunDeviceManager.GetUplinkAsync(ct);
        var nic=NetworkInterface.GetAllNetworkInterfaces().First(n=>n.Name==uplink.Name);
        if(nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)
        {
            var ssid=(await LinuxCommand.RunAsync("iwgetid",[uplink.Name,"--raw"],ct)).Trim();
            if(ssid.Length==0)throw new IOException("Не удалось прочитать SSID.");
            return new("wifi:"+Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(ssid)),"Wi-Fi · "+ssid);
        }
        return new("adapter:"+nic.GetPhysicalAddress(),"Сетевой интерфейс · "+nic.Name);
    }
}
