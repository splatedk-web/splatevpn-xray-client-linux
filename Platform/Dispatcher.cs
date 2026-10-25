using System.Collections.Concurrent;

namespace System.Windows.Threading
{
    // Serialized model event loop. Async continuations return to the same thread.
    public sealed class Dispatcher : SynchronizationContext
    {
        private readonly BlockingCollection<Action> queue = new();
        private readonly Thread thread;
        public bool HasShutdownStarted { get; private set; }
        public Dispatcher()
        {
            thread = new Thread(() => { SetSynchronizationContext(this); foreach (var a in queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "Splate model" };
            thread.Start();
        }
        public override void Post(SendOrPostCallback d, object? state) => queue.Add(() => d(state));
        public bool CheckAccess() => Thread.CurrentThread == thread;
        public Task InvokeAsync(Action action) => InvokeAsync(() => { action(); return true; });
        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Run() { try { tcs.SetResult(action()); } catch (Exception e) { tcs.SetException(e); } }
            if (CheckAccess()) Run(); else queue.Add(Run);
            return tcs.Task;
        }
        public void Invoke(Action action) => InvokeAsync(action).GetAwaiter().GetResult();
        public void BeginInvoke(Action action) => _ = InvokeAsync(action);
        public void BeginInvoke(DispatcherPriority priority, Action action) => BeginInvoke(action);
    }
    public enum DispatcherPriority { Background }
    public sealed class DispatcherTimer
    {
        private Timer? timer;
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public void Start() { Stop(); timer = new Timer(_ => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Tick?.Invoke(this, EventArgs.Empty)), null, Interval, Interval); }
        public void Stop() { timer?.Dispose(); timer = null; }
    }
}

// Data-only compatibility types for properties retained from the desktop model.
// Windowing, dialogs and clipboard are implemented by the Linux shell.
namespace System.Windows
{
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public Threading.Dispatcher Dispatcher { get; } = new();
        public object? MainWindow { get; set; }
        public Action? ShutdownRequested { get; set; }
        public void Shutdown() => ShutdownRequested?.Invoke();
    }
    public record struct Point(double X, double Y);
    public enum MessageBoxButton { OK, YesNo, OKCancel }
    public enum MessageBoxImage { Information, Warning, Error, Question }
    public static class Clipboard
    {
        public static string GetText() => SplateClientDesktop.Services.LinuxCommand.Run(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null ? "wl-paste" : "xclip",
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null ? ["--no-newline"] : ["-selection", "clipboard", "-o"]);
        public static void SetText(string text) => SplateClientDesktop.Services.LinuxCommand.Run(
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null ? "wl-copy" : "xclip",
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null ? [] : ["-selection", "clipboard"], text);
    }
}
namespace System.Windows.Media
{
    public record struct Color(string Hex);
    public static class ColorConverter { public static object ConvertFromString(string s) => new Color(s); }
    public sealed class SolidColorBrush(Color color) { public Color Color => color; public void Freeze() { } }
    public sealed class PointCollection : List<System.Windows.Point> { }
}
