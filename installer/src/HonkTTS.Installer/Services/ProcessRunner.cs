using System.Diagnostics;
using System.Text;

namespace HonkTTS.Installer.Services;

public sealed class ProcessRunner
{
    private Process? _active;
    private readonly object _lock = new();

    public void KillActive()
    {
        lock (_lock)
        {
            try
            {
                if (_active is { HasExited: false })
                    _active.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort — process may have exited between check and kill.
            }
        }
    }

    public async Task RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        Dictionary<string, string>? envVars = null,
        bool silent = false)
    {
        var result = await RunCoreAsync(fileName, arguments, workingDirectory, envVars, silent);

        if (result.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdOut
                : result.StdErr;

            throw new InvalidOperationException(
                $"Process '{fileName}' exited with code {result.ExitCode}.\n{output}");
        }
    }

    public async Task<ProcessResult> RunWithResultAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        Dictionary<string, string>? envVars = null)
    {
        return await RunCoreAsync(fileName, arguments, workingDirectory, envVars, silent: true);
    }

    private async Task<ProcessResult> RunCoreAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Dictionary<string, string>? envVars,
        bool silent)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (envVars is not null)
        {
            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.TrySetResult(); return; }
            stdout.AppendLine(e.Data);
            if (!silent)
                Console.WriteLine($"    {e.Data}");
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.TrySetResult(); return; }
            stderr.AppendLine(e.Data);
            if (!silent)
                Console.WriteLine($"    {e.Data}");
        };

        lock (_lock) { _active = process; }

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        // Wait for pipe readers to drain (null sentinel) before reading ExitCode.
        await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

        lock (_lock) { _active = null; }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}

public readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);
