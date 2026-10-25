using System.Diagnostics;
namespace SplateClientDesktop.Services;
// Linux supervisor mode owns the child and terminates it if its parent dies.
public sealed class ChildProcessJob : IDisposable
{
    private Process? process;
    public void Attach(Process p) => process = p;
    public void Dispose() { try { if (process is { HasExited: false }) process.Kill(true); } catch (InvalidOperationException) { } process = null; }
}
