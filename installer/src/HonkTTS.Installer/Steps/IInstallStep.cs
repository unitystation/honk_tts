using HonkTTS.Installer.Models;

namespace HonkTTS.Installer.Steps;

public interface IInstallStep
{
    string Name { get; }

    bool ShouldRun(InstallConfig config);

    Task ExecuteAsync(InstallConfig config);
}
