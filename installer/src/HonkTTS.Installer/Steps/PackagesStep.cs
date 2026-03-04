using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

public sealed class PackagesStep(ProcessRunner runner) : IInstallStep
{
    public string Name => "Python packages";

    public bool ShouldRun(InstallConfig config)
    {
        // Check if TTS package is installed (the heaviest dependency).
        // site-packages is under Lib/ on Windows, lib/python3.10/ on Unix.
        var sitePackages = PlatformInfo.IsWindows
            ? Path.Combine(config.VenvDir, "Lib", "site-packages", "TTS")
            : Path.Combine(config.VenvDir, "lib", "python3.10", "site-packages", "TTS");

        if (!Directory.Exists(sitePackages))
            return true;

        var installed = config.InstalledManifest;
        if (installed is null)
            return true; // Recover from interrupted installs without config.json

        return installed.RequirementsHash != config.ExpectedManifest.RequirementsHash;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        var sourceReqs = Path.Combine(config.SourceServerDir, "requirements.txt");
        if (!File.Exists(sourceReqs))
            throw new FileNotFoundException($"requirements.txt not found at {sourceReqs}");

        var tempReqs = Path.Combine(config.TempDir, "requirements.txt");
        Directory.CreateDirectory(config.TempDir);
        File.Copy(sourceReqs, tempReqs, overwrite: true);

        Console.WriteLine("    Installing packages (this may take several minutes)...");
        await runner.RunAsync(
            config.VenvPipExe,
            $"install -r \"{tempReqs}\" --extra-index-url {PlatformInfo.PYTORCH_INDEX_URL}",
            config.VenvDir);

        File.Delete(tempReqs);

        PatchEspeakWrapper(config);
    }

    /// <summary>
    /// Patches Coqui TTS's espeak_wrapper.py to tolerate non-UTF-8 output from eSpeak.
    /// eSpeak outputs in the system code page (e.g., CP1250 for Polish) which isn't valid UTF-8.
    /// Without this patch, importing TTS crashes with UnicodeDecodeError on affected systems.
    /// </summary>
    private static void PatchEspeakWrapper(InstallConfig config)
    {
        var sitePackages = PlatformInfo.IsWindows
            ? Path.Combine(config.VenvDir, "Lib", "site-packages")
            : Path.Combine(config.VenvDir, "lib", "python3.10", "site-packages");

        var wrapper = Path.Combine(sitePackages,
            "TTS", "tts", "utils", "text", "phonemizers", "espeak_wrapper.py");

        if (!File.Exists(wrapper))
        {
            Console.WriteLine("    Skipped espeak_wrapper patch (file not found)");
            return;
        }

        var content = File.ReadAllText(wrapper);
        var patched = content;

        // Patch 1: _espeak_exe() - subprocess.run with strict encoding="utf8"
        patched = patched.Replace(
            """encoding="utf8", check=True""",
            """encoding="utf8", errors="replace", check=True""");

        // Patch 2: get_espeakng_version() - subprocess.getoutput doesn't support errors=,
        // so replace with subprocess.run equivalent that does.
        patched = patched.Replace(
            """subprocess.getoutput("espeak-ng --version")""",
            """subprocess.run("espeak-ng --version", shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace").stdout""");

        // Patch 3: get_espeak_version() - same issue with the legacy espeak binary
        patched = patched.Replace(
            """subprocess.getoutput("espeak --version")""",
            """subprocess.run("espeak --version", shell=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8", errors="replace").stdout""");

        // Patch 4: All remaining manual .decode("utf8") calls — in supported_languages(),
        // phonemize_espeak(), and version(). These iterate raw bytes from the espeak
        // ctypes interface, which uses the system code page on Windows (e.g. CP1250).
        patched = patched.Replace(
            """.decode("utf8")""",
            """.decode("utf8", errors="replace")""");

        if (patched == content)
        {
            Console.WriteLine("    espeak_wrapper already patched (or pattern not found)");
            return;
        }

        File.WriteAllText(wrapper, patched);
        Console.WriteLine("    Patched espeak_wrapper.py (added errors=\"replace\")");
    }
}
