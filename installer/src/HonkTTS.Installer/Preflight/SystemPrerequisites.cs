using System.Diagnostics;
using HonkTTS.Installer.Models;

namespace HonkTTS.Installer.Preflight;

internal static class SystemPrerequisites
{
    public static void EnsureSystemEspeakPrerequisite()
    {
        if (PlatformInfo.IsWindows)
            return;

        if (TryProbeEspeak(out _))
            return;

        PrintWarningArt();
        Console.WriteLine("You must install espeak-ng before running this installer.");
        Console.WriteLine();
        Console.WriteLine("Detected issue:");
        Console.WriteLine($"  {GetProbeFailureReason()}");

        if (PlatformInfo.IsLinux)
        {
            var (name, command) = GetLinuxEspeakInstallInstruction();
            Console.WriteLine($"Detected distro: {name}");
            Console.WriteLine($"Install command: {command}");
        }
        else
        {
            Console.WriteLine("Detected platform: macOS");
            Console.WriteLine("Install command: brew install espeak-ng");
        }

        Console.WriteLine("May god help you if you have an immutable system");
        throw new InvalidOperationException(
            "eSpeak NG is not available on PATH. Install it first, then rerun the installer.");
    }

    private static string GetProbeFailureReason()
    {
        var path = ResolveOnPath("espeak-ng") ?? ResolveOnPath("espeak");
        if (path is null)
            return "espeak-ng/espeak binary was not found on PATH.";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return $"Failed to start '{path}'.";

            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                return $"'{path} --version' timed out.";
            }

            var stderr = process.StandardError.ReadToEnd().Trim();
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            return process.ExitCode == 0
                ? $"'{path}' appears runnable."
                : $"'{path} exists but failed to run: {output}";
        }
        catch (Exception ex)
        {
            return $"'{path}' exists but failed to execute: {ex.Message}";
        }
    }

    private static (string distroName, string installCommand) GetLinuxEspeakInstallInstruction()
    {
        string id = "";
        string idLike = "";
        string prettyName = "Linux";

        const string osRelease = "/etc/os-release";
        if (File.Exists(osRelease))
        {
            foreach (var raw in File.ReadAllLines(osRelease))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;

                var key = line[..idx];
                var value = line[(idx + 1)..].Trim().Trim('"');

                if (key.Equals("ID", StringComparison.OrdinalIgnoreCase))
                    id = value.ToLowerInvariant();
                else if (key.Equals("ID_LIKE", StringComparison.OrdinalIgnoreCase))
                    idLike = value.ToLowerInvariant();
                else if (key.Equals("PRETTY_NAME", StringComparison.OrdinalIgnoreCase))
                    prettyName = value;
            }
        }

        bool MatchAny(params string[] tokens) =>
            tokens.Any(t => id.Contains(t, StringComparison.Ordinal) || idLike.Contains(t, StringComparison.Ordinal));

        if (MatchAny("arch", "manjaro", "endeavouros"))
            return (prettyName, "sudo pacman -S espeak-ng");
        if (MatchAny("debian", "ubuntu", "linuxmint", "pop", "elementary", "raspbian"))
            return (prettyName, "sudo apt-get update && sudo apt-get install -y espeak-ng");
        if (MatchAny("fedora", "rhel", "centos", "rocky", "alma"))
            return (prettyName, "sudo dnf install -y espeak-ng");
        if (MatchAny("opensuse", "sles", "suse"))
            return (prettyName, "sudo zypper install -y espeak-ng");
        if (MatchAny("alpine"))
            return (prettyName, "sudo apk add espeak-ng");

        return (prettyName, "Install package 'espeak-ng' using your distro package manager.");
    }

    private static void PrintWarningArt()
    {
        Console.WriteLine(
            """
            @@@@@@@@@@@+++++@@@@@@@@@@@@+++++@@@@@@@@@@@
            @@@@@@@@@++++++++++%@@@@%++++++++++@@@@@@@@@
            @@@@@@@#++++++++++########*+++++++++*@@@@@@@
            @@@@@@@#++++++++++%%%@@%%#++++++++++#@@@@@@@
            @@@@@@@@@+++++++###-.%%.-###+++++++@@@@@@@@@
            @@@@@@@@@@@+++=+%#@@%@@%%@##++++=@@@@@@@@@@@
            @@@@@@@@@@@@@@**##@#*::**@#%**@@@@@@@@@@@@@@
            @@@@@@@@@@@@@@@@%%#%@**@%#%%@@@@@@@@@@@@@@@@
            @@@@@@@@@@@@@@##**########**##@@@@@@@@@@@@@@
            @@@@@@@@@@@@%+-=%#:#@@@%#:#%=-+#@@@@@@@@@@@@
            @@@@@@@@@@@:::==--%=:@@:-%--==:::@@@@@@@@@@@
            @@@@@@@@@::###==--::::::::--==#%%::@@@@@@@@@
            @@@@@@@@@:::::==--::::::::--==:::::@@@@@@@@@
            @@@@@@@*:%#:::==--########--==:::##:+@@@@@@@
            @@@@@@@*::::=@==--::::::::--==@+::::+@@@@@@@
            @@@@@@@*::::=@==--%%%%%%%#--==@+::::+@@@@@@@
            @@@@@@@#*%%#**==--::::::::--==**##%*#@@@@@@@
            @@@@@@@#*#####--########%##%--#####*#@@@@@@@
            @@@@@@@#*####*::==-:::::--==::*####*#@@@@@@@
            @@@@@@:::***+-:::::::::::::::::+***:::@@@@@@
            @@@@@@::::::::::::::::::::::::::::::::@@@@@@
            @@@@@@::::::::::::::::::::::::::::::::@@@@@@
            @@@@@@@*::::::::::::::::::::::::::::+@@@@@@@
            @@@@@@@@@::::::::::::..:::..:::::::@@@@@@@@@
            @@@@@@@@@@@@#:::::::::::::::::@@@@@@@@@@@@@@
            @@@@@@@@@@@@@@:::::::@@:::::::@@@@@@@@@@@@@@
            @@@@:::::::@#:::%%%=:@@:-%%%:::#@:::::::@@@@
            @%-:----:==:::-=**%=:@@:-%**==:::==:----:-%@
            @::::::::::::::::::::@@::::::::::::::::::::@
            """);
    }

    private static string? ResolveOnPath(string command)
    {
        if (Path.IsPathRooted(command) && File.Exists(command))
            return command;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var separators = new[] { Path.PathSeparator };
        foreach (var dir in path.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool TryProbeEspeak(out string resolvedPath)
    {
        resolvedPath = ResolveOnPath("espeak-ng") ?? ResolveOnPath("espeak") ?? "";
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
