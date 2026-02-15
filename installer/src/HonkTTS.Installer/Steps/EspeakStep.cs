using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

/// <summary>
/// Ensures eSpeak NG is available.
/// - Windows: downloads the MSI from GitHub and extracts it portably via msiexec /a.
/// - Linux/macOS: checks that espeak-ng is system-installed.
/// </summary>
public sealed class EspeakStep(DownloadService downloader, ProcessRunner runner) : IInstallStep
{
    public string Name => "eSpeak NG";

    public bool ShouldRun(InstallConfig config)
    {
        if (!PlatformInfo.RequiresEspeakDownload)
            return !IsEspeakOnPath();

        if (!File.Exists(config.EspeakExe))
            return true;

        var installed = config.InstalledManifest;
        if (installed is null)
            return true; // Recover from interrupted installs without config.json

        return installed != null &&
               installed.EspeakVersion != config.ExpectedManifest.EspeakVersion;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        if (PlatformInfo.IsWindows)
        {
            await DownloadAndExtractWindows(config);
            return;
        }

        if (!IsEspeakOnPath())
            throw new InvalidOperationException(
                "eSpeak NG is required but not found on your system.");
    }

    private async Task DownloadAndExtractWindows(InstallConfig config)
    {
        Directory.CreateDirectory(config.TempDir);
        var msiPath = Path.Combine(config.TempDir, "espeak-ng-X64.msi");

        Console.WriteLine("    Downloading eSpeak NG 1.51...");
        await downloader.DownloadFileAsync(PlatformInfo.EspeakMsiUrl, msiPath);

        var extractDir = Path.Combine(config.TempDir, "espeak-extract");
        Console.WriteLine("    Extracting eSpeak NG...");
        await runner.RunAsync(
            "msiexec",
            $"/a \"{msiPath}\" /qn TARGETDIR=\"{extractDir}\"",
            config.TempDir,
            silent: true);

        var sourceDir = FindFileInTree(extractDir, "espeak-ng.exe");

        PrepareEspeakDir(config);
        CopyDirectory(sourceDir, config.EspeakDir);

        Directory.Delete(extractDir, recursive: true);
        File.Delete(msiPath);
    }

    private static bool IsEspeakOnPath()
    {
        return ResolveOnPath("espeak-ng") is not null || ResolveOnPath("espeak") is not null;
    }

    private static void PrepareEspeakDir(InstallConfig config)
    {
        if (Directory.Exists(config.EspeakDir))
            Directory.Delete(config.EspeakDir, recursive: true);

        Directory.CreateDirectory(config.EspeakDir);
    }

    private static string FindFileInTree(string root, string fileName)
    {
        var candidates = Directory.GetFiles(root, fileName, SearchOption.AllDirectories);

        if (candidates.Length == 0)
            throw new FileNotFoundException($"Could not find {fileName} in {root}");

        return Path.GetDirectoryName(candidates[0])!;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            File.Copy(file, Path.Combine(destDir, relative), overwrite: true);
        }
    }

    private static string? ResolveOnPath(string command)
    {
        if (Path.IsPathRooted(command) && File.Exists(command))
            return command;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
