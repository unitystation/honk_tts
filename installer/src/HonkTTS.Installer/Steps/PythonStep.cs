using System.Formats.Tar;
using System.IO.Compression;
using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

public sealed class PythonStep(DownloadService downloader) : IInstallStep
{
    public string Name => "Python 3.10.13 (standalone)";

    public bool ShouldRun(InstallConfig config)
    {
        if (!File.Exists(config.PythonExe))
            return true;

        var installed = config.InstalledManifest;
        return installed != null &&
               installed.PythonVersion != config.ExpectedManifest.PythonVersion;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        // If Python version is changing, the venv is tied to the old binary — nuke it
        if (Directory.Exists(config.VenvDir))
        {
            Console.WriteLine("    Removing old venv (Python version changed)...");
            Directory.Delete(config.VenvDir, recursive: true);
        }

        Directory.CreateDirectory(config.TempDir);
        var archivePath = Path.Combine(config.TempDir, PlatformInfo.PythonArchiveFileName);

        Console.WriteLine($"    Downloading Python 3.10.13 standalone...");
        await downloader.DownloadFileAsync(PlatformInfo.PythonArchiveUrl, archivePath);

        Console.WriteLine("    Extracting to python/...");
        if (Directory.Exists(config.PythonDir))
            Directory.Delete(config.PythonDir, recursive: true);

        // python-build-standalone archives are .tar.gz containing a top-level "python/" dir.
        // We extract to BaseDir so that "python/" lands at install_dir/python/.
        await ExtractTarGzAsync(archivePath, config.BaseDir);

        File.Delete(archivePath);

        if (!OperatingSystem.IsWindows())
            SetExecutable(config.PythonExe);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void SetExecutable(string path)
    {
        File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destDir)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzipStream, destDir, overwriteFiles: true);
    }
}
