using HonkTTS.Installer.Models;
using HonkTTS.Installer.Services;

namespace HonkTTS.Installer.Steps;

/// <summary>
/// Triggers the Coqui TTS model download by running a small Python script
/// that instantiates the model. This ensures the model is cached before first use.
/// </summary>
public sealed class ModelWarmupStep(ProcessRunner runner) : IInstallStep
{
    public string Name => "TTS model download";

    public bool ShouldRun(InstallConfig config)
    {
        var modelSlug = config.ExpectedManifest.TtsModel.Replace("/", "--");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] cacheDirs =
        [
            Path.Combine(home, ".local", "share", "tts", modelSlug),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "tts", modelSlug),
            Path.Combine(home, "Library", "Application Support", "tts", modelSlug),
        ];

        var cached = cacheDirs.Any(dir =>
            Directory.Exists(dir) && Directory.GetFiles(dir, "*.pth").Length > 0);

        if (!cached)
            return true;

        var installed = config.InstalledManifest;
        return installed != null &&
               installed.TtsModel != config.ExpectedManifest.TtsModel;
    }

    public async Task ExecuteAsync(InstallConfig config)
    {
        Console.WriteLine("    Downloading and caching TTS model (this may take a few minutes)...");

        var env = BuildEspeakEnv(config);

        // Write warmup script to a temp file to avoid cross-platform shell escaping issues
        var scriptPath = Path.Combine(config.TempDir, "warmup.py");
        Directory.CreateDirectory(config.TempDir);
        await File.WriteAllTextAsync(scriptPath,
            """
            from TTS.api import TTS
            tts = TTS("tts_models/en/vctk/vits", progress_bar=True, gpu=False)
            print("Model loaded successfully.")
            """);

        await runner.RunAsync(config.VenvPythonExe, $"\"{scriptPath}\"", config.VenvDir, env);

        File.Delete(scriptPath);
    }

    private static Dictionary<string, string>? BuildEspeakEnv(InstallConfig config)
    {
        if (!PlatformInfo.RequiresEspeakDownload)
            return null; // system-installed espeak (macOS brew), no env needed

        var pathSep = PlatformInfo.IsWindows ? ";" : ":";
        var env = new Dictionary<string, string>
        {
            ["ESPEAK_DATA_PATH"] = config.EspeakDataDir,
            ["PATH"] = $"{config.EspeakDir}{pathSep}{Environment.GetEnvironmentVariable("PATH")}",
        };

        // Linux needs LD_LIBRARY_PATH for the portable shared library
        if (PlatformInfo.IsLinux)
        {
            var existingLdPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
            env["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existingLdPath)
                ? config.EspeakDir
                : $"{config.EspeakDir}:{existingLdPath}";
        }

        return env;
    }
}
