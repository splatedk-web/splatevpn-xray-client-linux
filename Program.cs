using Photino.NET;
using SplateClientDesktop;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;
using System.Windows;

if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("This build targets Linux. Publish: dotnet publish -r linux-x64 --self-contained"); return; }
if(InstanceControl.SendAsync(args.Contains("--toggle")?"toggle":"show").GetAwaiter().GetResult())return;
AppPaths.Initialize();
var configDir=Path.Combine(AppPaths.ConfigRoot,"SplateVPN");
Directory.CreateDirectory(configDir);
using var instance=new FileStream(Path.Combine(configDir,"linux.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var dispatcher=Application.Current.Dispatcher;
MainWindow? bridge=null;
LinuxTray? tray=null;
var quitting=false;
try
{
    SystemProxyManager.RecoverStaleProxy();
    var storage=new ServerStorage();
    var core=new XrayProcessManager(AppContext.BaseDirectory);
    var monitor=new TrafficMonitorService();
    var viewModel=dispatcher.InvokeAsync(()=>new MainViewModel(storage,core,new ConfigExportImportService(storage),monitor)).GetAwaiter().GetResult();
    var window=new PhotinoWindow().SetTitle("SplateVPN").SetUseOsDefaultSize(false).SetSize(1180,800).SetMinSize(820,620).Center().SetLogVerbosity(0);
    bridge=dispatcher.InvokeAsync(()=>new MainWindow(viewModel)).GetAwaiter().GetResult();
    Application.Current.MainWindow=bridge;
    bridge.Send=message=>window.SendWebMessage(message);
    bridge.WindowCommand=command=>window.Invoke(()=> {
        switch(command) {
            case "close": if(viewModel.MinimizeToTray) window.SetMinimized(true); else { quitting=true;window.Close(); } break;
            case "minimize": window.SetMinimized(true); break;
            case "maximize": window.SetMaximized(!window.Maximized); break;
            case "widget": window.SetMinSize(320,210).SetSize(360,230).SetTopMost(true); window.SendWebMessage("{\"type\":\"linuxWidget\",\"payload\":true}"); break;
            case "restore": window.SetMinSize(820,620).SetSize(1180,800).SetTopMost(false);window.SendWebMessage("{\"type\":\"linuxWidget\",\"payload\":false}");break;
        }
    });
    Application.Current.ShutdownRequested=()=>window.Invoke(()=>{quitting=true;window.Close();});
    window.RegisterWindowCreatedHandler((_,_)=> {
        try { tray=new LinuxTray(Path.Combine(AppContext.BaseDirectory,"Assets","app.png"),
            ()=>window.SetMinimized(false),
            ()=>dispatcher.BeginInvoke(()=>viewModel.ToggleConnectionCommand.Execute(null)),
            ()=>{quitting=true;window.Close();}); }
        catch(DllNotFoundException) { Console.Error.WriteLine("Tray library unavailable"); }
    });
    window.RegisterWindowClosingHandler((_,_)=> {
        if(!quitting && viewModel.MinimizeToTray) { window.SetMinimized(true);return true; }
        return false;
    });
    using var control=new InstanceControl(command=> {
        if(command=="toggle")dispatcher.BeginInvoke(()=>viewModel.ToggleConnectionCommand.Execute(null));
        else window.Invoke(()=>window.SetMinimized(false));
    });
    using var hotkey=new LinuxHotkey(()=>viewModel.Settings.EnableGlobalHotkey,()=>dispatcher.BeginInvoke(()=>viewModel.ToggleConnectionCommand.Execute(null)));
    var smokeIndex=Array.IndexOf(args,"--smoke-report");
    var smokeReport=smokeIndex>=0&&smokeIndex+1<args.Length?Path.GetFullPath(args[smokeIndex+1]):null;
    window.RegisterWebMessageReceivedHandler((_,message)=> {
        if(smokeReport!=null) {
            using var doc=System.Text.Json.JsonDocument.Parse(message);
            if(doc.RootElement.TryGetProperty("action",out var action)&&action.GetString()=="linuxSmoke") {
                File.WriteAllText(smokeReport,doc.RootElement.GetProperty("report").GetRawText());return;
            }
        }
        dispatcher.BeginInvoke(()=>bridge.Receive(message));
    });
    if(args.Contains("--minimized")||viewModel.Settings.StartMinimized)window.SetMinimized(true);
    window.Load(Path.Combine(AppContext.BaseDirectory,"wwwroot","index.html"));
    window.WaitForClose();
    tray?.Dispose();
}
catch(Exception error) {
    File.AppendAllText(Path.Combine(configDir,"crash.log"),DateTimeOffset.Now+" "+error+"\n");
    Console.Error.WriteLine(error);
    Environment.ExitCode=1;
}
finally {
    if(bridge!=null)dispatcher.Invoke(bridge.Dispose);
    SystemProxyManager.DisableProxy();
}
