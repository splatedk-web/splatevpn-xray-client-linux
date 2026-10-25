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
            var ssid = "";
            try
            {
                var uuid=await LinuxCommand.RunAsync("nmcli",["-g","GENERAL.CON-UUID","device","show",uplink.Name],ct);
                if(Guid.TryParse(uuid,out _)) ssid=await LinuxCommand.RunAsync("nmcli",["--escape","no","-g","802-11-wireless.ssid","connection","show",uuid],ct);
            }
            catch(Exception e) when(e is System.ComponentModel.Win32Exception or IOException) { }
            if(string.IsNullOrWhiteSpace(ssid))
                try { ssid=(await LinuxCommand.RunAsync("iwgetid",[uplink.Name,"--raw"],ct)).Trim(); }
                catch(Exception e) when(e is System.ComponentModel.Win32Exception or IOException) { }
            if(!string.IsNullOrWhiteSpace(ssid)) return new("wifi:"+Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(ssid)),"Wi-Fi · "+ssid);
            return new("adapter:"+nic.GetPhysicalAddress(),"Сетевой интерфейс · "+nic.Name+" (SSID недоступен)");
        }
        return new("adapter:"+nic.GetPhysicalAddress(),"Сетевой интерфейс · "+nic.Name);
    }
}
