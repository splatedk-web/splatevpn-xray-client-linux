using SplateClientDesktop.Services;
internal static class ProxyChecks
{
    public static void Run()
    {
        var before=SystemProxyManager.ReadStatus();
        try {
            SystemProxyManager.EnableProxy(39088,39089);
            if(!SystemProxyManager.ReadStatus().Manual)throw new Exception("GNOME proxy not enabled");
            var original=SystemProxyManager.InspectForTun();
            SystemProxyManager.SuspendForTun(original);
            if(SystemProxyManager.ReadStatus().HasConflict)throw new Exception("GNOME proxy not suspended for TUN");
            SystemProxyManager.DisableProxy();
            if(SystemProxyManager.ReadStatus()!=before)throw new Exception("GNOME proxy not restored");
            SystemProxyManager.EnableProxy(39088,39089);
            LinuxCommand.Run("gsettings",["set","org.gnome.system.proxy","mode","'auto'"]);
            SystemProxyManager.DisableProxy();
            if(!SystemProxyManager.ReadStatus().Pac)throw new Exception("Changed user proxy overwritten");
            Console.WriteLine("PASS GNOME proxy: enable, TUN suspend, restore, preserve independent user changes");
        }
        finally {SystemProxyManager.DisableProxy();}
    }
}
