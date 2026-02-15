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

    // Linux: .deb packages from Debian repos (extracted portably, no root needed).
    // Includes the eSpeak binary, phoneme data, and known runtime shared libs.
    private const string DebianEspeakBase = "https://deb.debian.org/debian/pool/main/e/espeak-ng";
    private const string DebianEspeakVersion = "1.52.0+dfsg-5+b1";
    private const string DebianPcaudioBase = "https://deb.debian.org/debian/pool/main/p/pcaudiolib";
    private const string DebianPcaudioVersion = "1.3-1+b1";
    private const string DebianSonicBase = "https://deb.debian.org/debian/pool/main/s/sonic";
    private const string DebianSonicVersion = "0.2.0-13+b2";
    private const string DebianPortAudioBase = "https://deb.debian.org/debian/pool/main/p/portaudio19";
    private const string DebianPortAudioVersion = "19.7.0-1";

    public static string[] EspeakLinuxDebUrls
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
            return
            [
                $"{DebianEspeakBase}/espeak-ng_{DebianEspeakVersion}_{arch}.deb",
                $"{DebianEspeakBase}/libespeak-ng1_{DebianEspeakVersion}_{arch}.deb",
                $"{DebianEspeakBase}/espeak-ng-data_{DebianEspeakVersion}_{arch}.deb",
                $"{DebianPcaudioBase}/libpcaudio0_{DebianPcaudioVersion}_{arch}.deb",
                $"{DebianSonicBase}/libsonic0_{DebianSonicVersion}_{arch}.deb",
                $"{DebianPortAudioBase}/libportaudio2_{DebianPortAudioVersion}_{arch}.deb",
            ];
        }
    }

    public static bool RequiresEspeakDownload => IsWindows || IsLinux;

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
