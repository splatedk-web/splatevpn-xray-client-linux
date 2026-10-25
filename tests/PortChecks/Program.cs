using SplateClientDesktop.Services;
AppPaths.Initialize();
if(args.Contains("--updates-only")){await UpdateChecks.RunAsync();return;}
if(args.Contains("--updates-live")){var result=await UpdateCheckerService.CheckForUpdatesAsync(args.Last());Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));if(!result.CheckSucceeded)Environment.ExitCode=1;return;}
if(args.Contains("--proxy-only")){ProxyChecks.Run();return;}
await UpdateChecks.RunAsync();
BalancerChecks.Run();
await ViewModelChecks.RunAsync();
var root=args.Length>0?Path.GetFullPath(args[0]):AppContext.BaseDirectory;
await BalancerProbeChecks.RunAsync(root);
var entry=WindowsIntegrationService.DesktopEntry("/tmp/a path/$test%/SplateClient");
if(!entry.Contains("--minimized")||!entry.Contains("\\$")||!entry.Contains("%%"))throw new Exception("XDG escaping failed");
Console.WriteLine("PASS XDG startup executable quoting and minimized argument");
