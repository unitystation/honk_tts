using System.Formats.Tar;
using System.IO.Compression;
using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

/// <summary>
/// Ensures eSpeak NG is available.
/// - Windows: downloads the MSI from GitHub and extracts it portably via msiexec /a.
/// - Linux x64: downloads a fully static espeak-ng build (no glibc/audio deps), so it also
///   works inside the Steam runtime, Flatpak and immutable distros.
/// - Linux arm64/macOS: should install system wide or get fucked.
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

        return installed.EspeakVersion != config.ExpectedManifest.EspeakVersion;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        if (PlatformInfo.IsWindows)
        {
            await DownloadAndExtractWindows(config);
            return;
        }

        if (PlatformInfo.IsLinuxX64)
        {
            await DownloadAndExtractLinux(config);
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
        await downloader.DownloadFileAsync(PlatformInfo.ESPEAK_MSI_URL, msiPath);

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

    private async Task DownloadAndExtractLinux(InstallConfig config)
    {
        Directory.CreateDirectory(config.TempDir);
        var archivePath = Path.Combine(config.TempDir, "espeak-ng-linux-x64.tar.gz");

        Console.WriteLine("    Downloading eSpeak NG 1.51 (static build)...");
        await downloader.DownloadFileAsync(PlatformInfo.EspeakLinuxX64Url, archivePath,
            PlatformInfo.EspeakLinuxX64Sha256);

        var extractDir = Path.Combine(config.TempDir, "espeak-extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);

        Console.WriteLine("    Extracting eSpeak NG...");
        await using (var fileStream = File.OpenRead(archivePath))
        await using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            await TarFile.ExtractToDirectoryAsync(gzipStream, extractDir, overwriteFiles: true);

        var sourceDir = FindFileInTree(extractDir, "espeak-ng");

        // Temp dir lives under BaseDir, so this is a same-filesystem rename.
        if (Directory.Exists(config.EspeakDir))
            Directory.Delete(config.EspeakDir, recursive: true);
        Directory.Move(sourceDir, config.EspeakDir);

        if (!OperatingSystem.IsWindows())
            SetExecutable(config.EspeakExe);

        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        File.Delete(archivePath);

        await ValidateEspeak(config);
    }

    private async Task ValidateEspeak(InstallConfig config)
    {
        var env = new Dictionary<string, string> { ["ESPEAK_DATA_PATH"] = config.EspeakDataDir };
        var result = await runner.RunWithResultAsync(config.EspeakExe, "--voices=variant", config.BaseDir, env);

        if (result.ExitCode != 0 || !result.StdOut.Contains("variant", StringComparison.Ordinal))
        {
            var output = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException(
                $"Bundled eSpeak NG failed to run (exit code {result.ExitCode}).\n{output}");
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void SetExecutable(string path)
    {
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
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
