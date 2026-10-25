using System.Diagnostics;

namespace SplateClientDesktop.Services;

public static class LinuxCommand
{
    public static async Task<string> RunAsync(string file, IEnumerable<string> args, CancellationToken token = default, string? input = null)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input != null };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException($"Не удалось запустить {file}");
        var output = p.StandardOutput.ReadToEndAsync(token);
        var errors = p.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (input != null) { await p.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false); p.StandardInput.Close(); }
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (p.ExitCode != 0) throw new IOException($"{file}: {await errors.ConfigureAwait(false)}");
            return (await output.ConfigureAwait(false)).TrimEnd();
        }
        catch { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
    }
    public static string Run(string file, string[] args, string? input = null) => RunAsync(file, args, input: input).GetAwaiter().GetResult();
}
