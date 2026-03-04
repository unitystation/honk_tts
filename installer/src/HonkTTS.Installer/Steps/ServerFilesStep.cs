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
        if (PlatformInfo.IsWindows)
        {
            // Use %~dp0-relative paths so the bat file is pure ASCII and works
            // regardless of the system code page.
            // Convert to 8.3 short paths so eSpeak-NG (which uses narrow-char
            // fopen/stat) can resolve paths with diacritics (e.g. C:\Users\Łukasz\...).
            const string bat = """
                               @echo off
                               setlocal
                               set "INSTALL_DIR=%~dp0"
                               for %%I in ("%INSTALL_DIR%.") do set "INSTALL_DIR=%%~fsI\"
                               set "ESPEAK_DATA_PATH=%INSTALL_DIR%espeak-ng\espeak-ng-data"
                               set "PATH=%INSTALL_DIR%espeak-ng;%PATH%"
                               set PYTHONUTF8=1
                               echo Starting HonkTTS server on http://127.0.0.1:5234 ...
                               "%INSTALL_DIR%venv\Scripts\python.exe" "%INSTALL_DIR%server\tts_server.py"
                               """;

            File.WriteAllText(config.StartScript, bat);
            Console.WriteLine("    Generated start_tts.bat");
        }
        else if (PlatformInfo.IsLinux)
        {
            // Use $SCRIPT_DIR-relative paths for the same reason as Windows.
            const string sh = """
                              #!/usr/bin/env bash
                              SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
                              if [ -d "$SCRIPT_DIR/espeak-ng" ]; then
                                export ESPEAK_DATA_PATH="$SCRIPT_DIR/espeak-ng/espeak-ng-data"
                                export LD_LIBRARY_PATH="$SCRIPT_DIR/espeak-ng:$LD_LIBRARY_PATH"
                                export PATH="$SCRIPT_DIR/espeak-ng:$PATH"
                              fi
                              export PYTHONUTF8=1
                              echo "Starting HonkTTS server on http://127.0.0.1:5234 ..."
                              "$SCRIPT_DIR/venv/bin/python" "$SCRIPT_DIR/server/tts_server.py"
                              """;

            File.WriteAllText(config.StartScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.StartScript);

            Console.WriteLine("    Generated start_tts.sh");
        }
        else
        {
            // macOS: espeak-ng is system-installed via brew, no env vars needed
            const string sh = """
                              #!/usr/bin/env bash
                              SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
                              export PYTHONUTF8=1
                              echo "Starting HonkTTS server on http://127.0.0.1:5234 ..."
                              "$SCRIPT_DIR/venv/bin/python" "$SCRIPT_DIR/server/tts_server.py"
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
            const string bat = """
                               @echo off
                               setlocal
                               set "INSTALL_DIR=%~dp0"
                               for %%I in ("%INSTALL_DIR%.") do set "INSTALL_DIR=%%~fsI\"
                               set "ESPEAK_DATA_PATH=%INSTALL_DIR%espeak-ng\espeak-ng-data"
                               set "PATH=%INSTALL_DIR%espeak-ng;%PATH%"
                               set PYTHONUTF8=1
                               "%INSTALL_DIR%venv\Scripts\python.exe" "%INSTALL_DIR%server\test_server.py" %*
                               """;

            File.WriteAllText(config.TestScript, bat);
            Console.WriteLine("    Generated test_tts.bat");
        }
        else if (PlatformInfo.IsLinux)
        {
            const string sh = """
                              #!/usr/bin/env bash
                              SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
                              if [ -d "$SCRIPT_DIR/espeak-ng" ]; then
                                export ESPEAK_DATA_PATH="$SCRIPT_DIR/espeak-ng/espeak-ng-data"
                                export LD_LIBRARY_PATH="$SCRIPT_DIR/espeak-ng:$LD_LIBRARY_PATH"
                                export PATH="$SCRIPT_DIR/espeak-ng:$PATH"
                              fi
                              export PYTHONUTF8=1
                              "$SCRIPT_DIR/venv/bin/python" "$SCRIPT_DIR/server/test_server.py" "$@"
                              """;

            File.WriteAllText(config.TestScript, sh);
            if (!OperatingSystem.IsWindows())
                SetExecutable(config.TestScript);

            Console.WriteLine("    Generated test_tts.sh");
        }
        else
        {
            const string sh = """
                              #!/usr/bin/env bash
                              SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
                              export PYTHONUTF8=1
                              "$SCRIPT_DIR/venv/bin/python" "$SCRIPT_DIR/server/test_server.py" "$@"
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
