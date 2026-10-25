using System.Runtime.InteropServices;
namespace SplateClientDesktop.Services;

public sealed class LinuxHotkey : IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorHandler(IntPtr display,IntPtr error);
    private static readonly ErrorHandler RegistrationError=(_,_)=>{Console.Error.WriteLine("Super+Alt+V is already reserved by the desktop; use a custom desktop shortcut.");return 0;};
    public LinuxHotkey(Func<bool> enabled,Action toggle)
    {
        if(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")!=null)return;
        _=Task.Run(async()=>{
            var display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)return;
            var buffer=Marshal.AllocHGlobal(192);
            try {
                var root=XDefaultRootWindow(display);var key=XKeysymToKeycode(display,XStringToKeysym("v"));
                var previous=XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(RegistrationError));
                try {
                    foreach(uint locks in new uint[]{0,2,16,18})XGrabKey(display,key,8|64|locks,root,0,1,1);
                    XSync(display,0);
                } finally { XSetErrorHandler(previous); }
                XFlush(display);
                while(!lifetime.IsCancellationRequested) {
                    while(XPending(display)>0) { XNextEvent(display,buffer);if(Marshal.ReadInt32(buffer)==2&&enabled())toggle(); }
                    await Task.Delay(50,lifetime.Token);
                }
            }catch(OperationCanceledException){}
            finally{Marshal.FreeHGlobal(buffer);XCloseDisplay(display);}
        });
    }
    public void Dispose()=>lifetime.Cancel();
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern int XKeysymToKeycode(IntPtr display,nuint keysym);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(IntPtr display,int key,uint modifiers,nuint root,int owner,int pointer,int keyboard);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display,IntPtr buffer);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display,int discard);
}
