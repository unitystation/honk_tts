using System.Security.Cryptography;

namespace HonkTTS.Installer.Services;

public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _http;
    private const int MaxRetries = 3;
    private const int BufferSize = 81920;

    public DownloadService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public async Task<string> DownloadFileAsync(
        string url, string destPath, string? expectedSha256 = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var hash = await DownloadCoreAsync(url, destPath);

                if (expectedSha256 is not null &&
                    !string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SHA256 mismatch: expected {expectedSha256}, got {hash}");
                }

                return hash;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                Console.WriteLine();
                Console.WriteLine($"    Attempt {attempt} failed: {ex.Message}");
                Console.WriteLine($"    Retrying in {attempt * 2}s...");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));

                if (File.Exists(destPath))
                    File.Delete(destPath);
            }
        }

        throw new InvalidOperationException($"Download failed after {MaxRetries} attempts: {url}");
    }

    public async Task<string> DownloadStringAsync(string url)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await _http.GetStringAsync(url);
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                Console.WriteLine($"    Attempt {attempt} failed: {ex.Message}, retrying...");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }

        throw new InvalidOperationException($"Download failed after {MaxRetries} attempts: {url}");
    }

    private async Task<string> DownloadCoreAsync(string url, string destPath)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, useAsync: true);

        using var sha256 = SHA256.Create();
        using var hashStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write);

        var buffer = new byte[BufferSize];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await hashStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;
            ReportProgress(totalRead, totalBytes);
        }

        await hashStream.FlushFinalBlockAsync();
        Console.WriteLine();

        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private static void ReportProgress(long downloaded, long? total)
    {
        if (total.HasValue)
        {
            var pct = (double)downloaded / total.Value * 100;
            var dlMb = downloaded / 1048576.0;
            var totalMb = total.Value / 1048576.0;
            Console.Write($"\r    {pct,5:F1}%  ({dlMb:F1} / {totalMb:F1} MB)");
        }
        else
        {
            var dlMb = downloaded / 1048576.0;
            Console.Write($"\r    {dlMb:F1} MB downloaded");
        }
    }

    public void Dispose() => _http.Dispose();
}
