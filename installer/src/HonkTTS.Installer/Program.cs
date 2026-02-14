using System.Diagnostics;
using System.Text.Json;
using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;
using HonkTTS.Installer.Steps;

namespace HonkTTS.Installer;

public class Program
{
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
            Console.WriteLine("Interrupted — killing child processes...");
            runner.KillActive();
            Environment.Exit(130);
        };

        try
        {
            var config = InstallConfig.FromArgs(args);
            Console.WriteLine($"Install directory: {config.BaseDir}");
            Console.WriteLine($"Platform: {GetPlatformLabel()}");
            Console.WriteLine();

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
                    // Corrupt config.json — treat as fresh install
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

            IInstallStep[] steps =
            [
                new PythonStep(downloader),
                new VenvStep(runner),
                new PackagesStep(runner),
                new EspeakStep(downloader, runner),
                new ModelWarmupStep(runner),
                new ServerFilesStep(),
            ];

            var totalStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                var label = $"[{i + 1}/{steps.Length}] {step.Name}";

                if (!step.ShouldRun(config))
                {
                    Console.WriteLine($"{label} — skipped (already installed)");
                    continue;
                }

                Console.WriteLine($"{label}");
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
