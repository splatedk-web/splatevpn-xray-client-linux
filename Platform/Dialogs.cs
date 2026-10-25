using System.Text.Json;
using SplateClientDesktop.Services;
using SplateClientDesktop.Models;
namespace Microsoft.Win32
{
    public class OpenFileDialog
    {
        public string Filter { get; set; } = "";
        public string Title { get; set; } = "Выберите файл";
        public string FileName { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        protected virtual bool Saving => false;
        public bool? ShowDialog(object? owner = null)
        {
            var args = new List<string> {"--file-selection","--title="+Title};
            if(Saving) args.AddRange(["--save","--confirm-overwrite"]);
            if(FileName.Length>0) args.Add("--filename="+FileName);
            var result=LinuxDialogs.Try(args.ToArray()); if(result==null) return false;FileName=result;return true;
        }
    }
    public sealed class SaveFileDialog : OpenFileDialog { protected override bool Saving => true; }
}
namespace SplateClientDesktop.Services
{
    public static class LinuxDialogs
    {
        public static string? Try(params string[] args)
        {
            // zenity exit 1 means cancel; launch failures must remain visible.
            var psi=new System.Diagnostics.ProcessStartInfo("zenity"){UseShellExecute=false,RedirectStandardOutput=true};
            foreach(var arg in args) psi.ArgumentList.Add(arg);
            using var p=System.Diagnostics.Process.Start(psi)!;var result=p.StandardOutput.ReadToEnd();p.WaitForExit();
            if(p.ExitCode==1) return null;if(p.ExitCode!=0) throw new IOException("Не удалось открыть диалог zenity.");return result.TrimEnd();
        }
    }
    public class QrCodeScannerService
    {
        public Task<string?> ScanFromFileAsync(string path) => Task.Run<string?>(()=>LinuxCommand.Run("zbarimg",["--quiet","--raw",path]));
        public async Task<string?> ScanFromScreenAsync()
        {
            var path=Path.Combine(Path.GetTempPath(),"splate-qr-"+Guid.NewGuid()+".png");
            try { await LinuxCommand.RunAsync("gnome-screenshot",["-a","-f",path]);return await ScanFromFileAsync(path); }
            finally { File.Delete(path); }
        }
    }
}
namespace SplateClientDesktop.Views
{
    public class BackupPasswordWindow(bool exporting)
    {
        public object? Owner {get;set;}
        public string Passphrase {get;private set;}="";
        public bool? ShowDialog() {var p=LinuxDialogs.Try("--password","--title="+(exporting?"Пароль резервной копии":"Пароль для восстановления"));if(p==null)return false;Passphrase=p;return true;}
    }
    public class BackupRestoreWindow
    {
        public object? Owner{get;set;} public bool Merge{get;private set;}
        public bool? ShowDialog() {var r=LinuxDialogs.Try("--list","--radiolist","--title=Восстановление","--column=Выбор","--column=Режим","TRUE","Объединить","FALSE","Заменить");if(r==null)return false;Merge=r=="Объединить";return true;}
    }
    public class AddSubscriptionWindow
    {
        public object? Owner{get;set;} public SubscriptionItem? ResultSubscription{get;private set;}
        public bool? ShowDialog() {var r=LinuxDialogs.Try("--forms","--title=Подписка","--add-entry=Название","--add-entry=URL");if(r==null)return false;var p=r.Split('|',2);if(p.Length!=2||!Uri.TryCreate(p[1],UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))throw new ArgumentException("Некорректный URL подписки");ResultSubscription=new(){Name=p[0],Url=p[1]};return true;}
    }
    public class EditServerWindow(ServerNode? node)
    {
        public object? Owner{get;set;} public ServerNode? ResultNode{get;private set;}
        public bool? ShowDialog()
        {
            var value=LinuxDialogs.Try("--entry","--title=Параметры сервера","--text=Ссылка VPN (vless/vmess/trojan/ss/socks)","--entry-text="+(node==null?"":ServerShare.Link(node)));
            if(value==null)return false;var nodes=LinkParser.Parse(value); ResultNode=nodes;return nodes!=null;
        }
    }
}
