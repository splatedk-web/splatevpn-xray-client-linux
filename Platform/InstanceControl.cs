using System.Net.Sockets;
using System.Text;
namespace SplateClientDesktop.Services;

public sealed class InstanceControl : IDisposable
{
    private readonly Socket server;
    private readonly string path;
    private readonly CancellationTokenSource lifetime=new();
    public static string SocketPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(),"splatevpn-"+Environment.UserName+".sock");
    public static async Task<bool> SendAsync(string command)
    {
        try { using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath));await socket.SendAsync(Encoding.UTF8.GetBytes(command));return true; }
        catch(SocketException){return false;}
    }
    public InstanceControl(Action<string> handle)
    {
        path=SocketPath;
        if(File.Exists(path))File.Delete(path);
        server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(path));
        if(OperatingSystem.IsLinux())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        server.Listen(2);
        _=Task.Run(async()=>{
            try { while(!lifetime.IsCancellationRequested) {
                using var client=await server.AcceptAsync(lifetime.Token);
                var buffer=new byte[32];using var budget=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);budget.CancelAfter(1000);
                try { var size=await client.ReceiveAsync(buffer,SocketFlags.None,budget.Token);var command=Encoding.UTF8.GetString(buffer,0,size);if(command is "show" or "toggle")handle(command); }
                catch(OperationCanceledException) { }
            }}catch(OperationCanceledException){}catch(ObjectDisposedException){}
        });
    }
    public void Dispose(){lifetime.Cancel();server.Dispose();File.Delete(path);lifetime.Dispose();}
}
