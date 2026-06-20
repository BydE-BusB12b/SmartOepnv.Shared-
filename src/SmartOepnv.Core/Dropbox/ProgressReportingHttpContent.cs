using System.Net;
using System.Net.Http.Headers;

namespace SmartOepnv.Core.Dropbox;

internal sealed class ProgressReportingHttpContent : HttpContent
{
    private readonly byte[] _payload;
    private readonly IProgress<DropboxTransferProgress>? _progress;
    private readonly string _phase;
    private readonly TransferEtaEstimator _etaEstimator = new();

    public ProgressReportingHttpContent(
        byte[] payload,
        string phase,
        IProgress<DropboxTransferProgress>? progress)
    {
        _payload = payload;
        _phase = phase;
        _progress = progress;
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _payload.Length;
        return true;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        const int chunkSize = 81_920;
        long transferred = 0;
        var total = _payload.Length;

        Report(transferred, total);

        for (var offset = 0; offset < total; offset += chunkSize)
        {
            var count = (int)Math.Min(chunkSize, total - offset);
            await stream.WriteAsync(_payload.AsMemory(offset, count)).ConfigureAwait(false);
            transferred += count;
            Report(transferred, total);
        }
    }

    private void Report(long transferred, long total)
    {
        _progress?.Report(new DropboxTransferProgress
        {
            Phase = _phase,
            BytesTransferred = transferred,
            TotalBytes = total,
            EstimatedSecondsRemaining = _etaEstimator.EstimateSecondsRemaining(transferred, total)
        });
    }
}
