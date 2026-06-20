namespace SmartOepnv.Core.Dropbox;

public sealed class DropboxTransferProgress
{
    public string Phase { get; init; } = string.Empty;

    public long BytesTransferred { get; init; }

    public long TotalBytes { get; init; }

    public double Percent =>
        TotalBytes > 0
            ? Math.Clamp(BytesTransferred * 100.0 / TotalBytes, 0, 100)
            : 0;

    public int? EstimatedSecondsRemaining { get; init; }

    public string PercentDisplay => $"{Percent:0}%";

    public string EtaDisplay =>
        EstimatedSecondsRemaining is > 0
            ? $"Noch ca. {DropboxTransferProgressFormatting.FormatDuration(EstimatedSecondsRemaining.Value)}"
            : TotalBytes > 0 && BytesTransferred > 0
                ? "Restzeit wird berechnet…"
                : string.Empty;
}

internal static class DropboxTransferProgressFormatting
{
    public static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds < 60)
        {
            return $"{totalSeconds} Sek.";
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return seconds == 0 ? $"{minutes} Min." : $"{minutes}:{seconds:D2} Min.";
    }
}

internal sealed class TransferEtaEstimator
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public int? EstimateSecondsRemaining(long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0 || bytesTransferred <= 0)
        {
            return null;
        }

        var elapsedSeconds = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
        if (elapsedSeconds < 0.8)
        {
            return null;
        }

        var rate = bytesTransferred / elapsedSeconds;
        if (rate < 64)
        {
            return null;
        }

        var remainingBytes = Math.Max(0, totalBytes - bytesTransferred);
        return (int)Math.Ceiling(remainingBytes / rate);
    }
}
