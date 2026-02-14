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
            $"install -r \"{tempReqs}\" --extra-index-url {PlatformInfo.PytorchIndexUrl}",
            config.VenvDir);

        File.Delete(tempReqs);
    }
}
