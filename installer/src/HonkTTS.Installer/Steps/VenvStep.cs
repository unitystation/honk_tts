using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

public sealed class VenvStep(ProcessRunner runner) : IInstallStep
{
    public string Name => "Virtual environment";

    public bool ShouldRun(InstallConfig config) => !File.Exists(config.VenvPythonExe);

    public async Task ExecuteAsync(InstallConfig config)
    {
        Console.WriteLine("    Creating virtual environment...");
        await runner.RunAsync(config.PythonExe, $"-m venv \"{config.VenvDir}\"", config.PythonDir);
    }
}
