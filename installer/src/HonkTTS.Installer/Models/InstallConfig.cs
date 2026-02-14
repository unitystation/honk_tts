namespace HonkTTS.Installer.Models;

public sealed class InstallConfig
{
    public string BaseDir { get; }

    public string PythonDir => Path.Combine(BaseDir, "python");
    public string VenvDir => Path.Combine(BaseDir, "venv");
    public string EspeakDir => Path.Combine(BaseDir, "espeak-ng");
    public string ServerDir => Path.Combine(BaseDir, "server");
    public string TempDir => Path.Combine(BaseDir, ".tmp");

    public string PythonExe => Path.Combine(PythonDir, PlatformInfo.PythonExeRelative);
    public string PipExe => Path.Combine(PythonDir, PlatformInfo.PipExeRelative);
    public string VenvPythonExe => Path.Combine(VenvDir, PlatformInfo.VenvPythonRelative);
    public string VenvPipExe => Path.Combine(VenvDir, PlatformInfo.VenvPipRelative);

    public string EspeakExe => Path.Combine(EspeakDir, PlatformInfo.IsWindows ? "espeak-ng.exe" : "espeak-ng");
    public string EspeakDataDir => Path.Combine(EspeakDir, "espeak-ng-data");

    public string StartScript => PlatformInfo.IsWindows
        ? Path.Combine(BaseDir, "start_tts.bat")
        : Path.Combine(BaseDir, "start_tts.sh");
    public string TestScript => PlatformInfo.IsWindows
        ? Path.Combine(BaseDir, "test_tts.bat")
        : Path.Combine(BaseDir, "test_tts.sh");
    public string MetadataFile => Path.Combine(BaseDir, "config.json");

    public string SourceServerDir { get; }

    public InstallManifest? InstalledManifest { get; set; }

    public InstallManifest ExpectedManifest { get; set; } = new();

    public static InstallConfig FromArgs(string[] args)
    {
        var baseDir = args.Length > 0
            ? args[0]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StationHub", "Installations", "tts");

        return new InstallConfig(baseDir);
    }

    private InstallConfig(string baseDir)
    {
        BaseDir = Path.GetFullPath(baseDir);

        // The server source is located relative to the installer binary or CWD.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "server"),
            Path.Combine(AppContext.BaseDirectory, "server"),
            Path.Combine(Directory.GetCurrentDirectory(), "server"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "server"),
        };

        SourceServerDir = candidates
            .Select(d => Path.GetFullPath(d))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "tts_server.py")))
            ?? throw new DirectoryNotFoundException(
                "Could not locate server/tts_server.py relative to the installer.");
    }
}
