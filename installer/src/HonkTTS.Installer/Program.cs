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
        PrintBanner();

        var runner = new ProcessRunner();
        SetupCancelHandler(runner);

        try
        {
            var cli = InstallerCli.ParseArgs(args);
            var config = InitializeConfig(cli);
            await LoadManifestsAsync(config);

            using var downloader = new DownloadService();
            var steps = BuildStepPipeline(cli, downloader, runner);

            var elapsed = await ExecuteStepsAsync(steps, config);

            CleanupTempFiles(config);
            PrintSuccess(config, elapsed);

            Environment.Exit(0);
            return 0;
        }
        catch (Exception ex)
        {
            PrintError(ex);
            Environment.Exit(1);
            return 1;
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("HonkTTS Installer");
        Console.WriteLine("=================");
        Console.WriteLine();
    }

    private static void SetupCancelHandler(ProcessRunner runner)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Interrupted - killing child processes...");
            runner.KillActive();
            Environment.Exit(130);
        };
    }

    private static InstallConfig InitializeConfig(CliOptions cli)
    {
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

        return config;
    }

    private static async Task LoadManifestsAsync(InstallConfig config)
    {
        if (File.Exists(config.MetadataFile))
        {
            try
            {
                config.InstalledManifest = JsonSerializer.Deserialize(
                    await File.ReadAllTextAsync(config.MetadataFile),
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
    }

    private static StepEntry[] BuildStepPipeline(
        CliOptions cli, DownloadService downloader, ProcessRunner runner)
    {
        StepEntry[] allSteps =
        [
            new("python", new PythonStep(downloader)),
            new("venv", new VenvStep(runner)),
            new("packages", new PackagesStep(runner)),
            new("espeak", new EspeakStep(downloader, runner)),
            new("warmup", new ModelWarmupStep(runner)),
            new("server", new ServerFilesStep()),
        ];

        var enabled = allSteps.Where(step =>
                (cli.OnlySteps.Count == 0 || cli.OnlySteps.Contains(step.Key)) &&
                !cli.SkipSteps.Contains(step.Key))
            .ToArray();

        if (enabled.Length == 0)
            throw new ArgumentException("No steps selected after applying --skip/--only filters.");

        return enabled;
    }

    private static async Task<TimeSpan> ExecuteStepsAsync(
        StepEntry[] steps, InstallConfig config)
    {
        var totalStopwatch = Stopwatch.StartNew();

        for (int i = 0; i < steps.Length; i++)
        {
            var stepEntry = steps[i];
            var step = stepEntry.Step;
            var label = $"[{i + 1}/{steps.Length}] {step.Name} ({stepEntry.Key})";

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

        totalStopwatch.Stop();
        return totalStopwatch.Elapsed;
    }

    private static void CleanupTempFiles(InstallConfig config)
    {
        if (Directory.Exists(config.TempDir))
            Directory.Delete(config.TempDir, recursive: true);
    }

    private static void PrintSuccess(InstallConfig config, TimeSpan elapsed)
    {
        Console.WriteLine("=================");
        Console.WriteLine($"Installation complete! ({elapsed.TotalSeconds:F0}s)");
        Console.WriteLine($"Start the server with: {config.StartScript}");
    }

    private static void PrintError(Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine($"Installation failed: {ex.Message}");
        Console.ResetColor();

#if DEBUG
        Console.WriteLine(ex.StackTrace);
#endif
    }

    private static string GetPlatformLabel()
    {
        if (PlatformInfo.IsWindows) return "Windows";
        return PlatformInfo.IsMacOs ? "macOS" : "Linux";
    }
}
