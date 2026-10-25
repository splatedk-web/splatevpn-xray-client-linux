using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace SplateClientDesktop.Services;

public record NetworkUplink(string Name, int Index, string Gateway);

public sealed class TunDeviceManager
{
    public const string DeviceName = "SplateTUN";
    private Process? process;
    private bool stopping;
    public bool IsRunning => process is { HasExited: false };
    public event Action<int>? OnProcessExited;
    // The installed polkit helper supplies the needed rights, never the GUI.
    public static bool IsAdministrator() => File.Exists("/usr/libexec/splatevpn-network");
    public static Task<string> RunAsync(string file, string[] args, CancellationToken token = default) => LinuxCommand.RunAsync(file, args, token);
    public static async Task<NetworkUplink> GetUplinkAsync(CancellationToken token = default)
    {
        using var routes = JsonDocument.Parse(await RunAsync("ip", ["-j", "-4", "route", "show", "table", "main", "default"], token));
        var route = routes.RootElement.EnumerateArray().Where(r => r.GetProperty("dev").GetString() != DeviceName)
            .OrderBy(r => r.TryGetProperty("metric", out var m) ? m.GetInt32() : 0).FirstOrDefault();
        if (route.ValueKind != JsonValueKind.Object) throw new IOException("Не найден физический шлюз IPv4.");
        var name = route.GetProperty("dev").GetString()!;
        var nic = NetworkInterface.GetAllNetworkInterfaces().First(n => n.Name == name);
        return new(name, nic.GetIPProperties().GetIPv4Properties().Index, route.TryGetProperty("gateway", out var gateway) ? gateway.GetString()! : "");
    }
    public async Task<bool> StartTunAsync(int port, NetworkUplink uplink, bool blockIpv6, Action<string> log, CancellationToken token)
    {
        StopTun(log); stopping = false;
        var psi = new ProcessStartInfo("pkexec") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/usr/libexec/splatevpn-network", port.ToString(), Environment.ProcessId.ToString() }) psi.ArgumentList.Add(arg);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true }; process = p;
        p.Exited += (_, _) => { if (!stopping && process == p) OnProcessExited?.Invoke(p.ExitCode); };
        p.Start();
        p.ErrorDataReceived += (_, e) => { if(e.Data != null) log("[TUN] " + e.Data); }; p.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            while (await p.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            { log("[TUN] " + line); if (line == "READY") return true; }
            throw new IOException("Сетевой помощник не запустился. Проверьте установку и запрос polkit.");
        }
        catch { StopTun(log); throw; }
    }
    public void StopTun(Action<string>? log = null)
    {
        stopping = true;
        var p = process; process = null;
        if(p == null) return;
        try { if (!p.HasExited) { p.StandardInput.Close(); if (!p.WaitForExit(5000)) log?.Invoke("[TUN] Ожидается очистка сетевого помощника."); } }
        finally { p.Dispose(); }
    }
}
