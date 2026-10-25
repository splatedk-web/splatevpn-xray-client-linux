using System.Text.Json;
namespace SplateClientDesktop.Services;

public static class WindowsIntegrationService
{
    public static string AutoStartPath => Path.Combine(AppPaths.ConfigRoot, "autostart", "splatevpn.desktop");
    public static bool IsAutoStartEnabled() => File.Exists(AutoStartPath);
    public static string DesktopEntry(string executable) => "[Desktop Entry]\nType=Application\nName=SplateVPN\nExec=\"" + executable.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%") + "\" --minimized\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
    public static void SetAutoStart(bool enable)
    {
        if (!enable) { File.Delete(AutoStartPath); return; }
        var exe = Environment.ProcessPath ?? throw new IOException("Не найден путь приложения.");
        if(Path.GetFileNameWithoutExtension(exe) == "dotnet") throw new IOException("Автозапуск доступен в опубликованной сборке. Запустите установленный SplateClient.");
        Directory.CreateDirectory(Path.GetDirectoryName(AutoStartPath)!);
        AtomicFileSet.Write(new Dictionary<string,byte[]> { [AutoStartPath] = System.Text.Encoding.UTF8.GetBytes(DesktopEntry(exe)) });
    }
}

public record ProxyPreflight(bool Manual, bool Pac, string Signature)
{
    public bool HasConflict => Manual || Pac;
    public static ProxyPreflight None => new(false,false,"");
}

// Desktop proxy ownership is durable and restored only while values are ours.
public static class SystemProxyManager
{
    private static readonly string SnapshotPath = Path.Combine(AppPaths.ConfigRoot, "SplateVPN", "linux-proxy.json");
    private record Snapshot(Dictionary<string,string> Before, Dictionary<string,string> Applied);
    private static Snapshot? saved;
    private static bool Kde => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE",StringComparison.OrdinalIgnoreCase);
    private static readonly string[] Keys = ["mode", "http.host", "http.port", "https.host", "https.port", "socks.host", "socks.port", "autoconfig-url", "ignore-hosts"];
    private static string Get(string key)
    {
        var (schema,name) = Location(key);
        return LinuxCommand.Run("gsettings", ["get", schema, name]);
    }
    private static (string,string) Location(string key) { var split=key.Split('.'); return split.Length==1 ? ("org.gnome.system.proxy",key) : ("org.gnome.system.proxy."+split[0],split[1]); }
    private static Dictionary<string,string> Read()
    {
        if(Kde) return new[] { "ProxyType","httpProxy","httpsProxy","socksProxy","NoProxyFor","Proxy Config Script" }.ToDictionary(k=>k,k=>LinuxCommand.Run("kreadconfig6",["--file","kioslaverc","--group","Proxy Settings","--key",k]));
        return Keys.ToDictionary(k=>k,Get);
    }
    private static void Write(Dictionary<string,string> values)
    {
        foreach(var (key,value) in values)
            if(Kde) LinuxCommand.Run("kwriteconfig6",["--file","kioslaverc","--group","Proxy Settings","--key",key,value]);
            else { var (schema,name)=Location(key); LinuxCommand.Run("gsettings",["set",schema,name,value]); }
    }
    private static ProxyPreflight Status(Dictionary<string,string> v) => new(Kde ? v["ProxyType"]=="1" : v["mode"]=="'manual'",Kde ? v["ProxyType"]=="2" : v["mode"]=="'auto'",JsonSerializer.Serialize(v));
    private static bool Equal(Dictionary<string,string> a,Dictionary<string,string> b) => a.Count==b.Count && a.All(p=>b.TryGetValue(p.Key,out var v)&&v==p.Value);
    private static void Apply(Dictionary<string,string> values)
    {
        var before=Read(); saved ??=new(before,values); saved=saved with {Applied=values};
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
        AtomicFileSet.Write(new Dictionary<string,byte[]> { [SnapshotPath]=JsonSerializer.SerializeToUtf8Bytes(saved) });
        try { Write(values); } catch { try { Write(before); } catch { } throw; }
    }
    public static void RecoverStaleProxy() { if(File.Exists(SnapshotPath)) { saved=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath)); DisableProxy(); } }
    public static void EnableProxy(int httpPort=10809,int socksPort=10808)
    {
        var v=Read();
        if(Kde) { v["ProxyType"]="1"; v["httpProxy"]=$"http://127.0.0.1 {httpPort}"; v["httpsProxy"]=v["httpProxy"]; v["socksProxy"]=$"socks://127.0.0.1 {socksPort}"; v["NoProxyFor"]="localhost,127.0.0.1,::1"; }
        else { v["mode"]="'manual'"; foreach(var protocol in new[]{"http","https","socks"}) { v[protocol+".host"]="'127.0.0.1'";v[protocol+".port"]=(protocol=="socks"?socksPort:httpPort).ToString(); } v["ignore-hosts"]="['localhost', '127.0.0.0/8', '::1']"; }
        Apply(v);
    }
    public static void DisableProxy()
    {
        if(saved==null) return;
        if(Equal(Read(),saved.Applied)) Write(saved.Before);
        saved=null; File.Delete(SnapshotPath);
    }
    public static ProxyPreflight ReadStatus() => Status(Read());
    public static ProxyPreflight InspectForTun() => Status(saved?.Before ?? Read());
    public static bool IsProxyEnabled() => ReadStatus().Manual;
    public static void SuspendForTun(ProxyPreflight approved)
    {
        if(InspectForTun().Signature!=approved.Signature) throw new IOException("Прокси изменился во время подключения.");
        var v=Read();v[Kde?"ProxyType":"mode"]=Kde?"0":"'none'";Apply(v);
    }
    public static HttpProxyEndpoint OriginalHttpProxy()
    {
        var v=saved?.Before??Read();
        if(!Status(v).Manual) throw new IOException("Ручной системный HTTP-прокси не включён.");
        if(Kde) { var parts=v["httpProxy"].Split(' ',StringSplitOptions.RemoveEmptyEntries);var u=new Uri(parts[0]);return UpstreamProxy.Validate(u.Host,parts.Length>1?int.Parse(parts[1]):u.Port); }
        return UpstreamProxy.Validate(v["http.host"].Trim('\''),int.Parse(v["http.port"]));
    }
}

public static class NativeThemeService { public static void Apply(string theme,double glass) { /* HTML theme owns all colors. */ } }
public static class WindowsServiceHelper
{
    public static bool InstallService(string core,string config)
    {
        string Quote(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("%","%%")+"\"";
        var dir=Path.Combine(AppPaths.ConfigRoot,"systemd","user");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"splatevpn.service"),"[Unit]\nDescription=SplateVPN proxy core\nAfter=network-online.target\n[Service]\nExecStart="+Quote(core)+" run -c "+Quote(config)+"\nRestart=on-failure\n[Install]\nWantedBy=default.target\n");
        LinuxCommand.Run("systemctl",["--user","daemon-reload"]);
        LinuxCommand.Run("systemctl",["--user","enable","--now","splatevpn.service"]);return true;
    }
    public static bool UninstallService() { LinuxCommand.Run("systemctl",["--user","disable","--now","splatevpn.service"]);return true; }
}
