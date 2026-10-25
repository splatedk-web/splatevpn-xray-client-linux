using System.Runtime.InteropServices;
namespace SplateClientDesktop.Services;

public sealed class LinuxTray : IDisposable
{
    private IntPtr indicator;
    private readonly List<Activate> callbacks = [];
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Activate(IntPtr widget,IntPtr data);
    public LinuxTray(string icon,Action show,Action toggle,Action exit)
    {
        indicator=app_indicator_new("splatevpn",icon,0);
        var menu=gtk_menu_new();
        Add(menu,"Открыть SplateVPN",show);Add(menu,"Подключить / отключить",toggle);Add(menu,"Выйти",exit);
        gtk_widget_show_all(menu);
        app_indicator_set_menu(indicator,menu);
        app_indicator_set_status(indicator,1);
    }
    private void Add(IntPtr menu,string label,Action action)
    {
        var item=gtk_menu_item_new_with_label(label);
        Activate callback=(_,_)=>{try{action();}catch(Exception e){Console.Error.WriteLine(e.Message);}};
        callbacks.Add(callback);g_signal_connect_data(item,"activate",callback,IntPtr.Zero,IntPtr.Zero,0);
        gtk_menu_shell_append(menu,item);
    }
    public void Dispose(){if(indicator!=IntPtr.Zero){app_indicator_set_status(indicator,0);g_object_unref(indicator);indicator=IntPtr.Zero;}}
    [DllImport("libayatana-appindicator3.so.1")] private static extern IntPtr app_indicator_new(string id,string icon,int category);
    [DllImport("libayatana-appindicator3.so.1")] private static extern void app_indicator_set_menu(IntPtr indicator,IntPtr menu);
    [DllImport("libayatana-appindicator3.so.1")] private static extern void app_indicator_set_status(IntPtr indicator,int status);
    [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_menu_new();
    [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_menu_item_new_with_label(string label);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_menu_shell_append(IntPtr menu,IntPtr child);
    [DllImport("libgtk-3.so.0")] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport("libgobject-2.0.so.0")] private static extern ulong g_signal_connect_data(IntPtr item,string signal,Activate callback,IntPtr data,IntPtr destroy,int flags);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr instance);
}
