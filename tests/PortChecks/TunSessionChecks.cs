using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using SplateClientDesktop;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

// Runs in a disposable network namespace with two real VLESS servers. The only
// platform substitution is polkit authorization: the test is already root.
internal static class TunSessionChecks
{
    const string Target = "http://203.0.113.1:39082/generate_204";
    public static async Task RunAsync(string root, int firstServerPid) =>
        await await Application.Current.Dispatcher.InvokeAsync(() => CheckAsync(root, firstServerPid));

    static async Task CheckAsync(string root, int firstServerPid)
    {
        var store = new ViewModelChecks.MemoryStorage();
        var a = new ServerNode { Id="tun-a", Name="Fixture A", Address="10.203.0.1", Port=39091, Protocol="vless", Security="none", Uuid="00000000-0000-0000-0000-000000000001", IsSelected=true };
        var b = new ServerNode { Id="tun-b", Name="Fixture B", Address="10.203.0.1", Port=39092, Protocol="vless", Security="none", Uuid=a.Uuid };
        store.Nodes=[a,b];
        var settings=store.LoadSettings();
        settings.EnableTunMode=true; settings.RoutingMode="global"; settings.AiRoutingMode="off";
        settings.EnableAdBlock=false; settings.BlockIpv6=false; settings.EnableSniffing=false;
        settings.CustomProxyRules="203.0.113.1/32"; // Documentation ranges are in geoip:private.
        settings.HttpPort=39109; settings.SocksPort=39108; settings.BalancerMode="failover";
        using var traffic=new TrafficMonitorService();
        var core=new XrayProcessManager(root);
        var platform=new FixturePlatform(root);
        var probes=new FixtureProbes(root);
        long clock=0, bytes=0;
        traffic.SessionBytesUpdated+=(down,up)=>Interlocked.Exchange(ref bytes,down+up);
        var vm=new MainViewModel(store,core,new ConfigExportImportService(store),traffic,false,platform,probes,new BalancerPolicy(()=>clock));
        using var bridge=new MainWindow(vm,false);
        var states=new List<JsonElement>();
        bridge.Send=text=>{using var doc=JsonDocument.Parse(text);var type=doc.RootElement.GetProperty("type").GetString();if(type=="state")states.Add(doc.RootElement.GetProperty("payload").Clone());else if(type is "settingsError" or "notification" or "confirmation")Console.WriteLine("IPC "+text);};
        try
        {
            await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Check(vm.IsConnected&&vm.ActiveTunMode&&!vm.HasPendingConnectionSettings,"real TUN startup publishes active state: "+vm.ConnectionDetail);
            Check(vm.ConnectionSteps.Any(s=>s.label.Contains("конфигурации Xray"))&&vm.ConnectionSteps.Any(s=>s.label.Contains("polkit"))&&vm.ConnectionSteps.Last().label.Contains("готовы"),"real startup reports validation, TUN authorization and readiness in order");
            Check(vm.ConnectionSteps.Zip(vm.ConnectionSteps.Skip(1)).All(p=>p.First.elapsedMs<=p.Second.elapsedMs),"connection stage timing is monotonic");
            var tunIndex=await LinuxCommand.RunAsync("cat",["/sys/class/net/SplateTUN/ifindex"]);
            await Transfer();
            await Task.Delay(1200);
            Check(Interlocked.Read(ref bytes)>0,"real Xray metrics count transferred data");
            var originalBytes=Interlocked.Read(ref bytes);
            await vm.SelectServerAsync(b); await Transfer();
            Check(vm.SelectedServer==b&&platform.Starts==1&&tunIndex==await LinuxCommand.RunAsync("cat",["/sys/class/net/SplateTUN/ifindex"]),"manual switch preserves TUN device and carries traffic");
            await vm.SelectServerAsync(a); await Transfer();
            vm.AutoFallbackFastestServer=true;
            Process.GetProcessById(firstServerPid).Kill(entireProcessTree:true);
            clock=10000;
            await Health(vm);
            Check(vm.IsConnected&&vm.SelectedServer==b&&vm.ActiveTunMode&&platform.Starts==1,"real outage automatically selects independently verified reserve");
            await Transfer();
            await Task.Delay(1200);
            Check(Interlocked.Read(ref bytes)>=originalBytes,"traffic totals survive actual core replacement");
            Console.WriteLine("PASS real TUN session: manual switch, killed VLESS server, automatic reserve, persistent device, traffic and metrics");

            // Exercise actual browser IPC instead of setting the model directly.
            bridge.Receive("{\"action\":\"saveSettings\",\"settings\":{\"enableTunMode\":false}}");
            await Applied(vm);
            Check(vm.IsConnected&&!vm.ActiveTunMode&&!vm.HasPendingConnectionSettings,"IPC TUN -> proxy applies immediately");
            Check(SystemProxyManager.ReadStatus().Manual,"proxy mode configures desktop proxy");
            Check(await probes.CurrentAsync(vm.ActiveHttpPort,2000,default)>0,"HTTP proxy carries traffic after mode change");
            bridge.Receive("{\"action\":\"setSetting\",\"key\":\"EnableTunMode\",\"value\":true}");
            await Applied(vm); await Transfer();
            Check(vm.ActiveTunMode&&!SystemProxyManager.ReadStatus().HasConflict,"legacy IPC proxy -> TUN applies and suspends own proxy");
            Check(states.Any(s=>s.GetProperty("isConnecting").GetBoolean())&&states.Last().GetProperty("activeTunMode").GetBoolean(),"IPC reports transition and actual adapter");

            vm.UpdateConnectivityStatus(false);
            Check(vm.StatusText=="СВЯЗЬ НЕ ПОДТВЕРЖДЕНА"&&vm.IsConnected,"failed connectivity is warning while tunnel stays enabled");
            vm.UpdateConnectivityStatus(true);
            Check(vm.StatusText=="ПОДКЛЮЧЕНО"&&vm.ConnectionDetail.Contains("TUN активен"),"successful recheck clears stale failure text");

            vm.Settings.ShareLan=true;vm.Settings.LanBindAddress="10.203.0.2";vm.Settings.LanAuthEnabled=true;vm.Settings.LanUsername="fixture";vm.Settings.LanPassword="fixture-password";
            await vm.SwitchServerAsync(b);
            Check(vm.IsConnected&&!vm.HasPendingConnectionSettings,"selected LAN address and credentials apply with TUN");
            var lanProbe="import http.client,base64\nfor authenticated in (False,True):\n c=http.client.HTTPConnection('10.203.0.2',39109,timeout=5)\n headers={'Proxy-Authorization':'Basic '+base64.b64encode(b'fixture:fixture-password').decode()} if authenticated else {}\n c.request('GET','"+Target+"',headers=headers)\n r=c.getresponse();assert r.status==(204 if authenticated else 407),(authenticated,r.status);r.read();c.close()";
            await LinuxCommand.RunAsync("ip",["netns","exec","splate-remote-qa","python3","-c",lanProbe]);
            Console.WriteLine("PASS LAN sharing: selected physical address, remote authenticated HTTP 204, unauthenticated 407, local TUN remains usable");
            await Transfer();

            core.Stop();
            // Notify the model through the same platform-exit path used by a failed helper.
            platform.ReportExit();
            await Until(()=>core.IsRunning&&!vm.IsConnecting,15000); await Transfer();
            Check(vm.IsConnected&&vm.ActiveTunMode,"unexpected exit recovers core with live TUN");
            vm.Disconnect();
            Check(!vm.IsConnected&&!vm.ActiveTunMode&&!core.IsRunning&&vm.StatusText=="ОТКЛЮЧЕНО","disconnect clears all active state");
            Check(!SystemProxyManager.ReadStatus().HasConflict,"disconnect restores initial desktop proxy");
            Console.WriteLine("PASS real session IPC: proxy/TUN transitions, recovery, health statuses, disconnect and proxy restoration");
        }
        catch { Console.Error.WriteLine(vm.Logs);throw; }
        finally { vm.Disconnect(); }
    }
    static Task Health(MainViewModel vm)=>(Task)typeof(MainViewModel).GetMethod("RunHealthCheckAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,null)!;
    static async Task Applied(MainViewModel vm) { await Task.Delay(500);await Until(()=>!vm.IsConnecting&&!vm.HasPendingConnectionSettings,20000); }
    static async Task Until(Func<bool> check,int timeout)
    { var clock=Stopwatch.StartNew();while(!check()){if(clock.ElapsedMilliseconds>timeout)throw new TimeoutException("Session transition did not complete");await Task.Delay(50);} }
    static async Task Transfer()
    { using var h=new HttpClientHandler {UseProxy=false};using var c=new HttpClient(h){Timeout=TimeSpan.FromSeconds(5)};using var r=await c.GetAsync(Target);Check(r.StatusCode==HttpStatusCode.NoContent,"unproxied application traffic traverses TUN"); }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static int Port(){using var l=new TcpListener(IPAddress.Loopback,0);l.Start();return ((IPEndPoint)l.LocalEndpoint).Port;}

    sealed class FixtureProbes(string root):IConnectionProbes
    {
        public bool NetworkAvailable=>true;
        public Task<long> CurrentAsync(int port,int timeout,CancellationToken ct)=>PingService.MeasureProxyLatencyToAsync(port,[Target],timeout,ct);
        public async Task CandidatesAsync(IReadOnlyList<ServerNode> nodes,Action<ServerNode,long> result,CancellationToken ct)
        {
            var route=await ProbeRoute.CaptureAsync(ct);
            foreach(var node in nodes)
            {
                int http=Port(),second=Port();
                var config=PingService.BuildProbeConfig([(node,http),(node,second)],route.Uplink?.Name);
                var manager=new XrayProcessManager(root);
                try { result(node,await manager.StartAsync(config.ToJsonString(),http,second,ct)?await CurrentAsync(http,1200,ct):-1); }
                finally { manager.Stop(); }
            }
            route.Validate();
        }
    }
    sealed class FixturePlatform(string root):IConnectionPlatform
    {
        Process? helper;
        public int Starts;
        public bool IsTunRunning=>helper is {HasExited:false};
        public bool IsAdministrator=>true;
        public event Action<int>? OnTunExited;
        public void ReportExit()=>OnTunExited?.Invoke(1);
        public Task<NetworkUplink> GetUplinkAsync(CancellationToken ct)=>TunDeviceManager.GetUplinkAsync(ct);
        public async Task<bool> StartTunAsync(int port,NetworkUplink uplink,bool block,Action<string> log,CancellationToken ct)
        {
            StopTun(log);Starts++;
            var script="import importlib.machinery; m=importlib.machinery.SourceFileLoader('network',"+JsonSerializer.Serialize(Path.Combine(root,"scripts/splatevpn-network"))+").load_module();m.CORE="+JsonSerializer.Serialize(Path.Combine(root,"core/tun2socks"))+";m.main()";
            var info=new ProcessStartInfo("python3"){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-u","-c",script,port.ToString(),Environment.ProcessId.ToString()})info.ArgumentList.Add(arg);
            helper=Process.Start(info)!;
            helper.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)log(e.Data);};helper.BeginErrorReadLine();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(10000);
            return await helper.StandardOutput.ReadLineAsync(timeout.Token)=="READY";
        }
        public void StopTun(Action<string> log){if(helper==null)return;helper.StandardInput.Close();if(!helper.WaitForExit(8000))helper.Kill(true);helper.Dispose();helper=null;}
        public void EnableProxy(int h,int s)=>SystemProxyManager.EnableProxy(h,s);
        public void DisableProxy()=>SystemProxyManager.DisableProxy();
        public ProxyPreflight InspectProxyForTun()=>SystemProxyManager.InspectForTun();
        public ProxyPreflight ReadProxyStatus()=>SystemProxyManager.ReadStatus();
        public void SuspendProxyForTun(ProxyPreflight p)=>SystemProxyManager.SuspendForTun(p);
        public void CheckEnvironment(AppSettings s,IReadOnlyCollection<int> ports)=>ConnectionPreflight.Check(root,s,ports);
    }
}
