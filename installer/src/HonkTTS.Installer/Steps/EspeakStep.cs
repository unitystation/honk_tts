using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

/// <summary>
/// Ensures eSpeak NG is available.
/// - Windows: downloads the MSI from GitHub and extracts it portably via msiexec /a.
/// - Linux: downloads .deb packages from Debian repos and extracts them portably (no root).
/// - macOS: checks that espeak-ng is system-installed (brew install espeak-ng).
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
            await DownloadAndExtractWindows(config);
        else if (PlatformInfo.IsLinux)
            await DownloadAndExtractLinux(config);
        else
        {
            throw new InvalidOperationException(
                "eSpeak NG is required but not found on your system.\n" +
                "    Install it with:\n" +
                "    brew install espeak-ng\n" +
                "    Then run the installer again.");
        }
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

    private async Task DownloadAndExtractLinux(InstallConfig config)
    {
        Directory.CreateDirectory(config.TempDir);
        var extractDir = Path.Combine(config.TempDir, "espeak-extract");
        Directory.CreateDirectory(extractDir);

        var debUrls = PlatformInfo.EspeakLinuxDebUrls;

        Console.WriteLine($"    Downloading eSpeak NG ({debUrls.Length} packages)...");
        foreach (var url in debUrls)
        {
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            var debPath = Path.Combine(config.TempDir, fileName);

            await downloader.DownloadFileAsync(url, debPath);

            // A .deb is an 'ar' archive. Extract it, then extract data.tar.* from inside.
            await ExtractDebAsync(debPath, extractDir);

            File.Delete(debPath);
        }

        // The extracted tree mirrors the Debian filesystem layout:
        //   usr/bin/espeak-ng
        //   usr/lib/x86_64-linux-gnu/libespeak-ng.so.1*
        //   usr/lib/x86_64-linux-gnu/espeak-ng-data/
        // We flatten the useful files into our espeak-ng/ directory.
        Console.WriteLine("    Assembling eSpeak NG directory...");
        PrepareEspeakDir(config);
        AssembleLinuxEspeak(extractDir, config.EspeakDir);

        Directory.Delete(extractDir, recursive: true);
    }

    private async Task ExtractDebAsync(string debPath, string extractDir)
    {
        var debWorkDir = Path.Combine(config_TempDir(debPath), Path.GetFileNameWithoutExtension(debPath));
        Directory.CreateDirectory(debWorkDir);

        await runner.RunAsync("ar", $"x \"{debPath}\"", debWorkDir, silent: true);

        var dataTar = Directory.GetFiles(debWorkDir, "data.tar.*").FirstOrDefault()
            ?? throw new FileNotFoundException($"No data.tar.* found in {debPath}");

        await runner.RunAsync("tar", $"xf \"{dataTar}\" -C \"{extractDir}\"", debWorkDir, silent: true);

        Directory.Delete(debWorkDir, recursive: true);
    }

    private static string config_TempDir(string debPath) => Path.GetDirectoryName(debPath)!;

    private static void AssembleLinuxEspeak(string extractDir, string espeakDir)
    {
        var binPath = Path.Combine(extractDir, "usr", "bin", "espeak-ng");
        if (File.Exists(binPath))
            File.Copy(binPath, Path.Combine(espeakDir, "espeak-ng"), overwrite: true);

        // The lib dir varies by architecture: x86_64-linux-gnu, aarch64-linux-gnu, etc.
        var usrLib = Path.Combine(extractDir, "usr", "lib");
        if (Directory.Exists(usrLib))
        {
            foreach (var soFile in Directory.GetFiles(usrLib, "libespeak-ng*", SearchOption.AllDirectories))
            {
                var destName = Path.GetFileName(soFile);
                File.Copy(soFile, Path.Combine(espeakDir, destName), overwrite: true);
            }
        }

        // It lives under usr/lib/<arch>/espeak-ng-data/ or usr/share/espeak-ng-data/.
        // Some extracted trees may also contain unrelated directories with this name
        // (e.g. documentation paths), so pick the one that looks like real runtime data.
        var dataDirs = Directory.GetDirectories(extractDir, "espeak-ng-data", SearchOption.AllDirectories);
        var selectedDataDir = SelectLinuxDataDir(dataDirs)
            ?? throw new DirectoryNotFoundException(
                $"Could not find a valid espeak-ng-data directory under {extractDir}.");

        var destData = Path.Combine(espeakDir, "espeak-ng-data");
        CopyDirectory(selectedDataDir, destData);
        EnsureEnUsVoiceCompatibility(destData);

        if (!HasEnUsVoice(destData))
        {
            throw new InvalidOperationException(
                $"eSpeak data is missing en-us voice after extraction: {destData}");
        }
    }

    private static string? SelectLinuxDataDir(string[] candidates)
    {
        if (candidates.Length == 0)
            return null;

        // Accept either:
        // 1) legacy voice layout with voices/en-us, or
        // 2) modern language layout with lang/gmw/en-US (we synthesize legacy voices later).
        foreach (var dir in candidates.OrderByDescending(static d => d.Length))
        {
            var voicesDir = Path.Combine(dir, "voices");
            var hasPhonTab = File.Exists(Path.Combine(dir, "phontab"));
            var hasVoices = Directory.Exists(voicesDir);
            var hasLangEnUs = File.Exists(Path.Combine(dir, "lang", "gmw", "en-US"));
            if (hasPhonTab && hasVoices && (HasEnUsVoice(dir) || hasLangEnUs))
                return dir;
        }

        return null;
    }

    private static bool HasEnUsVoice(string dataDir)
    {
        return File.Exists(Path.Combine(dataDir, "voices", "en", "en-us")) ||
               File.Exists(Path.Combine(dataDir, "voices", "en-us"));
    }

    private static void EnsureEnUsVoiceCompatibility(string dataDir)
    {
        if (HasEnUsVoice(dataDir))
            return;

        // Debian's espeak-ng-data can provide language definitions in lang/gmw/en-US
        // without the legacy voices/en-us entry expected by some phonemizer stacks.
        var langEnUs = Path.Combine(dataDir, "lang", "gmw", "en-US");
        if (!File.Exists(langEnUs))
            return;

        var voicesRoot = Path.Combine(dataDir, "voices");
        var voicesEn = Path.Combine(voicesRoot, "en");
        Directory.CreateDirectory(voicesRoot);
        Directory.CreateDirectory(voicesEn);

        var voiceBody = """
            name english-us
            language en-us
            """;

        File.WriteAllText(Path.Combine(voicesRoot, "en-us"), voiceBody);
        File.WriteAllText(Path.Combine(voicesEn, "en-us"), voiceBody);
    }

    private static bool IsEspeakOnPath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "espeak-ng",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;

            if (!p.WaitForExit(5000))
            {
                p.Kill(entireProcessTree: true);
                return false;
            }

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
}
