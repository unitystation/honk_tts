namespace HonkTTS.Installer;

internal sealed record CliOptions(
    string? BaseDir,
    HashSet<string> SkipSteps,
    HashSet<string> OnlySteps);

internal static class InstallerCli
{
    private static readonly string[] ValidStepKeys =
    [
        "python",
        "venv",
        "packages",
        "espeak",
        "warmup",
        "server",
    ];

    public static CliOptions ParseArgs(string[] args)
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
}
