using System.Diagnostics;
using HonkTTS.Installer.Models;

namespace HonkTTS.Installer.Preflight;

internal static class SystemPrerequisites
{
    public static void EnsureSystemEspeakPrerequisite()
    {
        if (PlatformInfo.IsWindows)
            return;

        if (HasEspeakOnPath())
            return;

        PrintWarningArt();
        Console.WriteLine("You must install espeak-ng before running this installer.");

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

    private static bool HasEspeakOnPath()
    {
        foreach (var candidate in new[] { "espeak-ng", "espeak" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process is null)
                    continue;

                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    continue;
                }

                if (process.ExitCode == 0)
                    return true;
            }
            catch
            {
                // Candidate not available.
            }
        }

        return false;
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
}
