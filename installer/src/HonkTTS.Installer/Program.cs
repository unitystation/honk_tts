using System.Diagnostics;
using System.Text.Json;
using HonkTTS.Installer.Models;
using HonkTTS.Installer.Preflight;
using HonkTTS.Installer.Services;
using HonkTTS.Installer.Steps;

namespace HonkTTS.Installer;

public class Program
{
    private sealed record StepEntry(string Key, IInstallStep Step);

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("HonkTTS Installer");
        Console.WriteLine("=================");
        Console.WriteLine();

        var exitCode = 1;
        var runner = new ProcessRunner();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination so we can clean up.
            Console.WriteLine();
            Console.WriteLine("Interrupted - killing child processes...");
            runner.KillActive();
            Environment.Exit(130);
        };

        try
        {
            var cli = InstallerCli.ParseArgs(args);
            var config = InstallConfig.FromArgs(cli.BaseDir is null ? [] : [cli.BaseDir]);
            Console.WriteLine($"Installer version: {config.ExpectedManifest.InstallerVersion}");
            Console.WriteLine($"Install directory: {config.BaseDir}");
            Console.WriteLine($"Platform: {GetPlatformLabel()}");
            if (cli.SkipSteps.Count > 0)
                Console.WriteLine($"CLI skips: {string.Join(", ", cli.SkipSteps.Order())}");
            if (cli.OnlySteps.Count > 0)
                Console.WriteLine($"CLI only: {string.Join(", ", cli.OnlySteps.Order())}");
            Console.WriteLine();
            SystemPrerequisites.EnsureSystemEspeakPrerequisite();

            Directory.CreateDirectory(config.BaseDir);

            if (File.Exists(config.MetadataFile))
            {
                try
                {
                    config.InstalledManifest = JsonSerializer.Deserialize(
                        File.ReadAllText(config.MetadataFile),
                        ManifestJsonContext.Default.InstallManifest);
                }
                catch
                {
                    // Corrupt config.json - treat as fresh install
                }
            }

            var reqsPath = Path.Combine(config.SourceServerDir, "requirements.txt");
            config.ExpectedManifest = new InstallManifest
            {
                RequirementsHash = File.Exists(reqsPath)
                    ? InstallManifest.ComputeFileHash(reqsPath)
                    : "",
            };

            using var downloader = new DownloadService();

            StepEntry[] allSteps =
            [
                new("python", new PythonStep(downloader)),
                new("venv", new VenvStep(runner)),
                new("packages", new PackagesStep(runner)),
                new("espeak", new EspeakStep(downloader, runner)),
                new("warmup", new ModelWarmupStep(runner)),
                new("server", new ServerFilesStep()),
            ];

            var enabledSteps = allSteps.Where(step =>
                    (cli.OnlySteps.Count == 0 || cli.OnlySteps.Contains(step.Key)) &&
                    !cli.SkipSteps.Contains(step.Key))
                .ToArray();

            if (enabledSteps.Length == 0)
                throw new ArgumentException("No steps selected after applying --skip/--only filters.");

            var totalStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < enabledSteps.Length; i++)
            {
                var stepEntry = enabledSteps[i];
                var step = stepEntry.Step;
                var label = $"[{i + 1}/{enabledSteps.Length}] {step.Name} ({stepEntry.Key})";

                if (!step.ShouldRun(config))
                {
                    Console.WriteLine($"{label} - skipped (already installed)");
                    continue;
                }

                Console.WriteLine(label);
                var sw = Stopwatch.StartNew();

                await step.ExecuteAsync(config);

                sw.Stop();
                Console.WriteLine($"    Done ({sw.Elapsed.TotalSeconds:F1}s)");
                Console.WriteLine();
            }

            if (Directory.Exists(config.TempDir))
                Directory.Delete(config.TempDir, recursive: true);

            totalStopwatch.Stop();
            Console.WriteLine("=================");
            Console.WriteLine($"Installation complete! ({totalStopwatch.Elapsed.TotalSeconds:F0}s)");
            Console.WriteLine($"Start the server with: {config.StartScript}");

            exitCode = 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"Installation failed: {ex.Message}");
            Console.ResetColor();

#if DEBUG
            Console.WriteLine(ex.StackTrace);
#endif
        }

        Environment.Exit(exitCode);
        return exitCode;
    }

    private static string GetPlatformLabel()
    {
        if (PlatformInfo.IsWindows) return "Windows";
        if (PlatformInfo.IsMacOS) return "macOS";
        return "Linux";
    }
}
