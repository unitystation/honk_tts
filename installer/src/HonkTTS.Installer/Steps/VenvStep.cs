using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

public sealed class VenvStep(ProcessRunner runner) : IInstallStep
{
    public string Name => "Virtual environment";

    public bool ShouldRun(InstallConfig config)
    {
        if (!File.Exists(config.VenvPythonExe))
            return true;

        // Recreate the venv when requirements changed so stale packages don't linger.
        var installed = config.InstalledManifest;
        return installed is not null
            && installed.RequirementsHash != config.ExpectedManifest.RequirementsHash;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        if (Directory.Exists(config.VenvDir))
        {
            Console.WriteLine("    Requirements changed - recreating virtual environment...");
            Directory.Delete(config.VenvDir, recursive: true);
        }
        else
        {
            Console.WriteLine("    Creating virtual environment...");
        }

        await runner.RunAsync(config.PythonExe, $"-m venv \"{config.VenvDir}\"", config.PythonDir);
    }
}
