using System.Text.Json;
using System.Text.Json.Serialization;

namespace HonkTTS.Installer.Models;

public sealed class InstallManifest
{
    public string InstallerVersion { get; set; } = "1.0.0";
    public string PythonVersion { get; set; } = "3.10.13";
    public string EspeakVersion { get; set; } = "1.51";
    public string TtsModel { get; set; } = "tts_models/en/vctk/vits";
    public string RequirementsHash { get; set; } = "";
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string InstallDir { get; set; } = "";

    public static string ComputeFileHash(string filePath)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filePath));
        return Convert.ToHexStringLower(bytes);
    }
}

[JsonSerializable(typeof(InstallManifest))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ManifestJsonContext : JsonSerializerContext;
