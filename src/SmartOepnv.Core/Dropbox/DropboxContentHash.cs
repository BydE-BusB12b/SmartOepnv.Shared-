using System.Security.Cryptography;

namespace SmartOepnv.Core.Dropbox;

/// <summary>
/// Dropbox-Inhaltsprüfsumme (SHA-256 je 4-MiB-Block, darüber noch einmal SHA-256).
/// </summary>
public static class DropboxContentHash
{
    public const int BlockSize = 4 * 1024 * 1024;

    public static string ComputeFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var overall = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BlockSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var block = read == buffer.Length
                ? buffer
                : buffer.AsSpan(0, read).ToArray();
            overall.AppendData(SHA256.HashData(block));
        }

        return Convert.ToHexString(overall.GetHashAndReset()).ToLowerInvariant();
    }
}
