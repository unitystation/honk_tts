using System.Diagnostics;
using System.Text.Json;
using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;
using HonkTTS.Installer.Steps;

namespace HonkTTS.Installer;

public class Program
{
    private sealed record CliOptions(
        string? BaseDir,
        HashSet<string> SkipSteps,
        HashSet<string> OnlySteps);

    private sealed record StepEntry(string Key, IInstallStep Step);

    private static readonly string[] ValidStepKeys =
    [
        "python",
        "venv",
        "packages",
        "espeak",
        "warmup",
        "server",
    ];

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
            var cli = ParseArgs(args);
            var config = InstallConfig.FromArgs(cli.BaseDir is null ? [] : [cli.BaseDir]);
            Console.WriteLine($"Installer version: {config.ExpectedManifest.InstallerVersion}");
            Console.WriteLine($"Install directory: {config.BaseDir}");
            Console.WriteLine($"Platform: {GetPlatformLabel()}");
            if (cli.SkipSteps.Count > 0)
                Console.WriteLine($"CLI skips: {string.Join(", ", cli.SkipSteps.Order())}");
            if (cli.OnlySteps.Count > 0)
                Console.WriteLine($"CLI only: {string.Join(", ", cli.OnlySteps.Order())}");
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

    private static CliOptions ParseArgs(string[] args)
    {
        string? baseDir = null;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help")
                PrintUsageAndExit();

            if (arg == "--skip" || arg == "--only")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}");

                var target = arg == "--skip" ? skip : only;
                AddStepList(target, args[++i], arg);
                continue;
            }

            if (arg.StartsWith("--skip=", StringComparison.OrdinalIgnoreCase))
            {
                AddStepList(skip, arg["--skip=".Length..], "--skip");
                continue;
            }

            if (arg.StartsWith("--only=", StringComparison.OrdinalIgnoreCase))
            {
                AddStepList(only, arg["--only=".Length..], "--only");
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown option: {arg}");

            if (baseDir is null)
            {
                baseDir = arg;
                continue;
            }

            throw new ArgumentException($"Unexpected positional argument: {arg}");
        }

        return new CliOptions(baseDir, skip, only);
    }

    private static void AddStepList(HashSet<string> target, string raw, string optionName)
    {
        var keys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keys.Length == 0)
            throw new ArgumentException($"No step keys provided for {optionName}");

        foreach (var key in keys)
        {
            if (!ValidStepKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Invalid step '{key}' for {optionName}. Valid: {string.Join(", ", ValidStepKeys)}");
            }

            target.Add(key.ToLowerInvariant());
        }
    }

    private static void PrintUsageAndExit()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  HonkTTS.Installer [install_dir] [--skip <steps>] [--only <steps>]");
        Console.WriteLine();
        Console.WriteLine("Step keys:");
        Console.WriteLine("  python, venv, packages, espeak, warmup, server");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  HonkTTS.Installer /root/tts --skip packages,warmup");
        Console.WriteLine("  HonkTTS.Installer /root/tts --only espeak");
        Environment.Exit(0);
    }

    private static string GetPlatformLabel()
    {
        if (PlatformInfo.IsWindows) return "Windows";
        if (PlatformInfo.IsMacOS) return "macOS";
        return "Linux";
    }
}
