namespace SplateClientDesktop.Services;
public static class SecurityIntegrationService {
 public static bool EnableKillSwitch(string path,int http,int socks) => throw new NotSupportedException("Kill switch недоступен в этой сборке.");
 public static void DisableKillSwitch() { }
 public static void SetDnsLeakProtection(bool enable) { if(enable)throw new NotSupportedException("Для системного DNS используйте TUN."); }
}
