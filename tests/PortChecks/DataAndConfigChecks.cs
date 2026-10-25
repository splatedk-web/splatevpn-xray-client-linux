using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class DataAndConfigChecks
{
    public static async Task RunAsync(string root)
    {
        var temp=Path.Combine(Path.GetTempPath(),"splate-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var storage=new ServerStorage(temp);
            var settings=new AppSettings { SettingsSchemaVersion=2, Theme="ocean", AutoStart=false, BalancerMode="failover", HttpPort=19080, SocksPort=19081 };
            storage.SaveSettings(settings);
            File.WriteAllText(Path.Combine(temp,"settings.json"),"{broken");
            var recovered=storage.LoadSettings();
            Check(recovered.Theme=="ocean"&&recovered.BalancerMode=="failover"&&storage.SettingsRecoveryNotice!=null&&Directory.GetFiles(temp,"*.damaged-*").Length==1,"damaged settings recovered with original preserved");
            var node=new ServerNode {Address="vpn.example",Port=443,Security="tls",Uuid="00000000-0000-0000-0000-000000000001"};
            storage.SaveCatalog([node],[new SubscriptionItem {Id="sub",Name="Fixture",Url="https://example.org/sub"}]);
            var backup=new ConfigExportImportService(storage);var file=Path.Combine(temp,"backup.splate");
            await backup.ExportAsync(file,"fixture-passphrase");
            storage.SaveServers([]);
            try {await backup.ImportAsync(file,"incorrect-password");throw new Exception("wrong password accepted");}catch(InvalidDataException){}
            Check(storage.LoadServers().Count==0,"failed backup import is atomic");
            await backup.ImportAsync(file,"fixture-passphrase");
            Check(storage.LoadServers().Single().Uuid==node.Uuid&&storage.LoadSettings().BalancerMode=="failover","encrypted backup roundtrip preserves servers/settings");
            var snapshot=ConnectionProfiles.Capture(recovered);
            recovered.Theme="sunset";recovered.BalancerMode="initial_only";recovered.HttpPort=20000;
            var target=ConnectionProfiles.Target(recovered,snapshot);
            Check(target.HttpPort==19080&&target.Theme=="sunset"&&target.BalancerMode=="initial_only","network profiles preserve appearance and balancer policy");
            var redacted=DiagnosticRedactor.Redact("vless://secret@example.org:443 token=private 203.0.113.1 "+node.Uuid,["example.org"]);
            Check(!redacted.Contains("private")&&!redacted.Contains(node.Uuid)&&!redacted.Contains("203.0.113.1"),"diagnostic credentials and IP redaction");
            Console.WriteLine("PASS data: corrupted settings recovery, encrypted export/import, rejected password, profile isolation, diagnostic redaction");

            var gen=new XrayConfigGenerator();int configurations=0;
            foreach(var protocol in new[]{"vless","vmess","trojan","shadowsocks"})
            foreach(var transport in new[]{"tcp","ws","grpc","xhttp","httpupgrade"})
            {
                node.Protocol=protocol;node.Network=transport;node.Encryption=protocol=="shadowsocks"?"aes-256-gcm":"none";
                var config=gen.GenerateConfig(node,mode:configurations%2==0?"global":"smart_ru",enableMux:true,enableTun:true,uplinkInterface:"eth0",enableAiDns:true,aiRoutingMode:"dns_vpn",customDohUrl:"https://dns.google/dns-query");
                var path=Path.Combine(temp,"validate.json");await File.WriteAllTextAsync(path,config);
                var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"core/xray")){RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in new[]{"run","-test","-format","json","-c",path})info.ArgumentList.Add(arg);
                info.Environment["XRAY_LOCATION_ASSET"]=Path.Combine(root,"core");
                using var process=System.Diagnostics.Process.Start(info)!;
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}catch{process.Kill(true);throw;}
                Check(process.ExitCode==0,$"{protocol}/{transport}: "+await output+await errors);configurations++;
            }
            Console.WriteLine($"PASS Xray validates {configurations} generated protocol/transport combinations with TUN, routing, DNS and Mux");

            using var client=new HttpClient(new BundledGeoHandler(root));
            var core=Path.Combine(root,"core");
            var asset=AdFilterService.EnsureAssets(root);
            Check(await GeoAssetUpdaterService.UpdateGeoAssetsAsync(core,client:client),"geodata updates into writable user assets");
            Check(File.Exists(Path.Combine(asset,"geo.user-updated")),"user geodata override is recorded");
            var original=File.ReadAllBytes(Path.Combine(asset,"geoip.dat"));
            var fake=Path.Combine(temp,"bundle");Directory.CreateDirectory(Path.Combine(fake,"core"));
            File.WriteAllText(Path.Combine(fake,"core/geoip.dat"),"different bundle");
            AdFilterService.EnsureAssets(fake);
            Check(File.ReadAllBytes(Path.Combine(asset,"geoip.dat")).SequenceEqual(original),"core startup preserves downloaded geodata");
            Check(await GeoAssetUpdaterService.RollbackAsync(core),"user geodata rollback validates and restores both files");
            using var invalid=new HttpClient(new BundledGeoHandler(root,true));
            Check(!await GeoAssetUpdaterService.UpdateGeoAssetsAsync(core,client:invalid),"invalid geodata checksum is rejected");
            Check(File.ReadAllBytes(Path.Combine(asset,"geoip.dat")).SequenceEqual(original),"failed update preserves working geodata");
            Console.WriteLine("PASS geodata: user-owned update, Xray validation, startup persistence, rollback, checksum failure preservation");
        }
        finally {Directory.Delete(temp,true);}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    sealed class BundledGeoHandler(string root,bool corrupt=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var name=Path.GetFileName(request.RequestUri!.AbsolutePath);bool hash=name.EndsWith(".sha256sum");
            var bytes=File.ReadAllBytes(Path.Combine(root,"core",hash?name[..^10]:name));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=hash?new StringContent(corrupt?new string('0',64):Convert.ToHexString(SHA256.HashData(bytes))):new ByteArrayContent(bytes)});
        }
    }
}
