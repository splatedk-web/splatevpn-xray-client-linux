using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class LanSharing
{
    public sealed record Interface(string address, string name);
    public static void Validate(AppSettings settings)
    {
        if (settings.LanBindAddress != "" && (!IPAddress.TryParse(settings.LanBindAddress, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)))
            throw new ArgumentException("Для раздачи выберите IPv4-адрес сетевого интерфейса.");
        if (settings.ShareLan && settings.LanAuthEnabled && (settings.LanBindAddress == "" || string.IsNullOrEmpty(settings.LanUsername) || string.IsNullOrEmpty(settings.LanPassword) || settings.LanUsername.Length is < 1 or > 64 || settings.LanPassword.Length is < 8 or > 128 ||
            settings.LanUsername.Any(char.IsControl) || settings.LanPassword.Any(char.IsControl)))
            throw new ArgumentException("Для авторизации выберите интерфейс, задайте логин и пароль от 8 символов.");
    }
    public static Interface[] Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            !n.Name.Equals(TunDeviceManager.DeviceName, StringComparison.OrdinalIgnoreCase) && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel && !n.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => new Interface(a.Address.ToString(), n.Name))).Distinct().OrderBy(n=>n.name).ThenBy(n=>n.address).ToArray();
}
