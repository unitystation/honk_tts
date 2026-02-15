using System.Text.Json;
using HonkTTS.Installer.Models;

namespace HonkTTS.Installer.Steps;

public sealed class ServerFilesStep : IInstallStep
{
    public string Name => "Server files and entrypoints";

    public bool ShouldRun(InstallConfig config) => true;

    public Task ExecuteAsync(InstallConfig config)
    {
        CopyServerFiles(config);
        GenerateEntrypoint(config);
        GenerateTestEntrypoint(config);
        WriteMetadata(config);
        return Task.CompletedTask;
    }

    private static void CopyServerFiles(InstallConfig config)
    {
        Directory.CreateDirectory(config.ServerDir);

        File.Copy(
            Path.Combine(config.SourceServerDir, "tts_server.py"),
            Path.Combine(config.ServerDir, "tts_server.py"),
            overwrite: true);
        Console.WriteLine("    Copied tts_server.py");

        // test_server.py (optional — not critical if missing)
        var testSource = Path.Combine(config.SourceServerDir, "scripts", "test_server.py");
        if (File.Exists(testSource))
        {
            var testDest = Path.Combine(config.ServerDir, "test_server.py");
            File.Copy(testSource, testDest, overwrite: true);
            Console.WriteLine("    Copied test_server.py");
        }
    }

    private static void GenerateEntrypoint(InstallConfig config)
    {
        var serverScript = Path.Combine(config.ServerDir, "tts_server.py");

        if (PlatformInfo.IsWindows)
        {
            var bat = $"""
                @echo off
                set ESPEAK_DATA_PATH={config.EspeakDataDir}
                set PATH={config.EspeakDir};%PATH%
                echo Starting HonkTTS server on http://127.0.0.1:5234 ...
                "{config.VenvPythonExe}" "{serverScript}"
                pause
                """;

            File.WriteAllText(config.StartScript, bat);
            Console.WriteLine("    Generated start_tts.bat");
        }
        else if (PlatformInfo.IsLinux)
        {
            // Linux: prefer system eSpeak; if a bundled espeak-ng dir exists, use it.
            var sh = $"""
                #!/usr/bin/env bash
                SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
                if [ -d "{config.EspeakDir}" ]; then
                  export ESPEAK_DATA_PATH="{config.EspeakDataDir}"
                  export LD_LIBRARY_PATH="{config.EspeakDir}:$LD_LIBRARY_PATH"
                  export PATH="{config.EspeakDir}:$PATH"
                fi
                echo "Starting HonkTTS server on http://127.0.0.1:5234 ..."
                "{config.VenvPythonExe}" "{serverScript}"
                """;

            File.WriteAllText(config.StartScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.StartScript);

            Console.WriteLine("    Generated start_tts.sh");
        }
        else
        {
            // macOS: espeak-ng is system-installed via brew, no env vars needed
            var sh = $"""
                #!/usr/bin/env bash
                echo "Starting HonkTTS server on http://127.0.0.1:5234 ..."
                "{config.VenvPythonExe}" "{serverScript}"
                """;

            File.WriteAllText(config.StartScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.StartScript);

            Console.WriteLine("    Generated start_tts.sh");
        }
    }

    private static void GenerateTestEntrypoint(InstallConfig config)
    {
        var testScript = Path.Combine(config.ServerDir, "test_server.py");

        if (!File.Exists(testScript))
        {
            Console.WriteLine("    Skipped test_tts entrypoint (test_server.py not found)");
            return;
        }

        if (PlatformInfo.IsWindows)
        {
            var bat = $"""
                @echo off
                set ESPEAK_DATA_PATH={config.EspeakDataDir}
                set PATH={config.EspeakDir};%PATH%
                "{config.VenvPythonExe}" "{testScript}" %*
                """;

            File.WriteAllText(config.TestScript, bat);
            Console.WriteLine("    Generated test_tts.bat");
        }
        else if (PlatformInfo.IsLinux)
        {
            var sh = $"""
                #!/usr/bin/env bash
                if [ -d "{config.EspeakDir}" ]; then
                  export ESPEAK_DATA_PATH="{config.EspeakDataDir}"
                  export LD_LIBRARY_PATH="{config.EspeakDir}:$LD_LIBRARY_PATH"
                  export PATH="{config.EspeakDir}:$PATH"
                fi
                "{config.VenvPythonExe}" "{testScript}" "$@"
                """;

            File.WriteAllText(config.TestScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.TestScript);

            Console.WriteLine("    Generated test_tts.sh");
        }
        else
        {
            var sh = $"""
                #!/usr/bin/env bash
                "{config.VenvPythonExe}" "{testScript}" "$@"
                """;

            File.WriteAllText(config.TestScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.TestScript);

            Console.WriteLine("    Generated test_tts.sh");
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

    private static void WriteMetadata(InstallConfig config)
    {
        // Write the expected manifest so next run can compare against it.
        // Preserve InstalledAt from previous install if it exists.
        var manifest = config.ExpectedManifest;
        manifest.InstallDir = config.BaseDir;
        manifest.UpdatedAt = DateTime.UtcNow;
        manifest.InstalledAt = config.InstalledManifest?.InstalledAt ?? DateTime.UtcNow;

        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallManifest);
        File.WriteAllText(config.MetadataFile, json);
        Console.WriteLine("    Wrote config.json");
    }
}
