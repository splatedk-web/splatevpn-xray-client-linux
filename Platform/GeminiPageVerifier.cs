using System.Diagnostics;
using System.Text.RegularExpressions;
namespace SplateClientDesktop.Services;
public static class GeminiPageVerifier
{
    public static async Task<AiDnsTargetResult> CheckAsync(int socksPort,CancellationToken token)
    {
        var clock=Stopwatch.StartNew();
        var profile=Path.Combine(Path.GetTempPath(),"splate-gemini-"+Guid.NewGuid());
        try
        {
            var chrome=new[]{"/usr/bin/chromium","/usr/bin/chromium-browser","/usr/bin/google-chrome"}.FirstOrDefault(File.Exists);
            if(chrome==null)return new("Gemini",AiDnsOutcome.Limited,Detail:"Для проверки JavaScript установите Chromium. Доступ не подтверждён.");
            var html=await LinuxCommand.RunAsync(chrome,["--headless=new","--disable-gpu","--disable-quic","--no-first-run","--user-data-dir="+profile,"--proxy-server=socks5://127.0.0.1:"+socksPort,"--proxy-bypass-list=<-loopback>","--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1","--virtual-time-budget=15000","--dump-dom","https://gemini.google.com/app?hl=en"],token);
            var text=System.Net.WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>"," "));
            return new("Gemini",AiDnsAutoSelector.ClassifyRendered(200,text,"https://gemini.google.com/app"),clock.ElapsedMilliseconds,"Публичная страница после JavaScript; аккаунт не проверялся");
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(Exception e){return new("Gemini",AiDnsOutcome.Failed,Detail:e.Message);}
        finally{try{if(Directory.Exists(profile))Directory.Delete(profile,true);}catch(IOException){} }
    }
}
