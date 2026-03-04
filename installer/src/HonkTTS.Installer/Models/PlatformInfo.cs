using System.Runtime.InteropServices;

namespace HonkTTS.Installer.Models;

public static class PlatformInfo
{
    /// <summary>
    /// On Windows, converts a path to its 8.3 short form (pure ASCII) so that
    /// native tools like eSpeak-NG (which use narrow-char fopen/stat) can resolve
    /// paths containing diacritics. Returns the original path on other platforms
    /// or if short-name generation is disabled on the volume.
    /// </summary>
    public static string ToShortPathOrOriginal(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;

        var sb = new System.Text.StringBuilder(260);
        var len = GetShortPathName(path, sb, (uint)sb.Capacity);
        if (len == 0 || len > sb.Capacity)
            return path; // 8.3 names disabled or error — fall back to original

        return sb.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string lpszLongPath, System.Text.StringBuilder lpszShortPath, uint cchBuffer);

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string PythonExeRelative => IsWindows ? "python.exe" : "bin/python3";
    public static string PipExeRelative => IsWindows ? "Scripts/pip.exe" : "bin/pip3";

    public static string VenvBinDir => IsWindows ? "Scripts" : "bin";
    public static string VenvPythonRelative => Path.Combine(VenvBinDir, IsWindows ? "python.exe" : "python3");
    public static string VenvPipRelative => Path.Combine(VenvBinDir, IsWindows ? "pip.exe" : "pip3");

    // Uses python-build-standalone (same source as uv/rye).
    // install_only builds are minimal, relocatable, and include pip.
    private const string PYTHON_VERSION = "3.10.13";
    private const string STANDALONE_RELEASE = "20240107";
    private const string STANDALONE_BASE_URL =
        $"https://github.com/astral-sh/python-build-standalone/releases/download/{STANDALONE_RELEASE}";

    public static string PythonArchiveUrl
    {
        get
        {
            var triple = GetPythonTriple();
            return $"{STANDALONE_BASE_URL}/cpython-{PYTHON_VERSION}+{STANDALONE_RELEASE}-{triple}-install_only.tar.gz";
        }
    }

    public static string PythonArchiveFileName
    {
        get
        {
            var triple = GetPythonTriple();
            return $"cpython-{PYTHON_VERSION}-{triple}.tar.gz";
        }
    }

    public const string ESPEAK_MSI_URL =
        "https://github.com/espeak-ng/espeak-ng/releases/download/1.51/espeak-ng-X64.msi";

    public static bool RequiresEspeakDownload => IsWindows;

    public const string PYTORCH_INDEX_URL = "https://download.pytorch.org/whl/cpu";

    private static string GetPythonTriple()
    {
        if (IsWindows)
            return "x86_64-pc-windows-msvc-shared";

        if (IsMacOs)
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
