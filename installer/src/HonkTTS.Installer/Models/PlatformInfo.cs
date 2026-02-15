using System.Runtime.InteropServices;

namespace HonkTTS.Installer.Models;

public static class PlatformInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string PythonExeRelative => IsWindows ? "python.exe" : "bin/python3";
    public static string PipExeRelative => IsWindows ? "Scripts/pip.exe" : "bin/pip3";

    public static string VenvBinDir => IsWindows ? "Scripts" : "bin";
    public static string VenvPythonRelative => Path.Combine(VenvBinDir, IsWindows ? "python.exe" : "python3");
    public static string VenvPipRelative => Path.Combine(VenvBinDir, IsWindows ? "pip.exe" : "pip3");

    // Uses python-build-standalone (same source as uv/rye).
    // install_only builds are minimal, relocatable, and include pip.
    private const string PythonVersion = "3.10.13";
    private const string StandaloneRelease = "20240107";
    private const string StandaloneBaseUrl =
        $"https://github.com/astral-sh/python-build-standalone/releases/download/{StandaloneRelease}";

    public static string PythonArchiveUrl
    {
        get
        {
            var triple = GetPythonTriple();
            return $"{StandaloneBaseUrl}/cpython-{PythonVersion}+{StandaloneRelease}-{triple}-install_only.tar.gz";
        }
    }

    public static string PythonArchiveFileName
    {
        get
        {
            var triple = GetPythonTriple();
            return $"cpython-{PythonVersion}-{triple}.tar.gz";
        }
    }

    public const string EspeakMsiUrl =
        "https://github.com/espeak-ng/espeak-ng/releases/download/1.51/espeak-ng-X64.msi";

    public static bool RequiresEspeakDownload => IsWindows;

    public const string PytorchIndexUrl = "https://download.pytorch.org/whl/cpu";

    private static string GetPythonTriple()
    {
        if (IsWindows)
            return "x86_64-pc-windows-msvc-shared";

        if (IsMacOS)
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "aarch64-apple-darwin"
                : "x86_64-apple-darwin";
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "aarch64-unknown-linux-gnu"
            : "x86_64-unknown-linux-gnu";
    }
}
