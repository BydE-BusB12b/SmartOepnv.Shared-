namespace SmartOepnv.Fahrgeraet;

public static class StereoWav
{
    public static void Play(byte[] source, bool? inside) =>
        TonAusgang.PlayAnnouncement(source, inside);

    public static void PlayHandset(byte[] source) =>
        TonAusgang.PlayDisplay(source);

    public static byte[] ToZonedWav(byte[] source, bool inside)
    {
        if (!TryPcm16(source, out var channels, out var rate, out var pcm))
        {
            return source;
        }

        var frames = pcm.Length / 2 / channels;
        var stereo = new byte[frames * 4];
        for (var i = 0; i < frames; i++)
        {
            var sample = channels == 1
                ? BitConverter.ToInt16(pcm, i * 2)
                : BitConverter.ToInt16(pcm, i * channels * 2);
            var left = inside ? sample : (short)0;
            var right = inside ? (short)0 : sample;
            BitConverter.TryWriteBytes(stereo.AsSpan(i * 4, 2), left);
            BitConverter.TryWriteBytes(stereo.AsSpan(i * 4 + 2, 2), right);
        }

        return WrapPcm(stereo, rate, 2);
    }

    public static byte[] ToBothChannels(byte[] source)
    {
        if (!TryPcm16(source, out var channels, out var rate, out var pcm))
        {
            return source;
        }

        var frames = pcm.Length / 2 / channels;
        var stereo = new byte[frames * 4];
        for (var i = 0; i < frames; i++)
        {
            var sample = BitConverter.ToInt16(pcm, i * channels * 2);
            BitConverter.TryWriteBytes(stereo.AsSpan(i * 4, 2), sample);
            BitConverter.TryWriteBytes(stereo.AsSpan(i * 4 + 2, 2), sample);
        }

        return WrapPcm(stereo, rate, 2);
    }

    private static bool TryPcm16(byte[] wav, out int channels, out int rate, out byte[] pcm)
    {
        channels = rate = 0;
        pcm = [];
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I')
        {
            return false;
        }

        var offset = 12;
        while (offset + 8 < wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, offset, 4);
            var size = BitConverter.ToInt32(wav, offset + 4);
            var data = offset + 8;
            if (id == "fmt " && size >= 16)
            {
                var format = BitConverter.ToInt16(wav, data);
                channels = BitConverter.ToInt16(wav, data + 2);
                rate = BitConverter.ToInt32(wav, data + 4);
                var bits = BitConverter.ToInt16(wav, data + 14);
                if (format != 1 || bits != 16 || channels is < 1 or > 2)
                {
                    return false;
                }
            }
            else if (id == "data")
            {
                pcm = wav.AsSpan(data, Math.Min(size, wav.Length - data)).ToArray();
                return channels > 0 && rate > 0 && pcm.Length > 0;
            }

            offset = data + size + (size % 2);
        }

        return false;
    }

    private static byte[] WrapPcm(byte[] pcm, int rate, int channels)
    {
        var bytes = new byte[44 + pcm.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), 36 + pcm.Length);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BitConverter.TryWriteBytes(bytes.AsSpan(16, 4), 16);
        BitConverter.TryWriteBytes(bytes.AsSpan(20, 2), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(22, 2), (short)channels);
        BitConverter.TryWriteBytes(bytes.AsSpan(24, 4), rate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28, 4), rate * channels * 2);
        BitConverter.TryWriteBytes(bytes.AsSpan(32, 2), (short)(channels * 2));
        BitConverter.TryWriteBytes(bytes.AsSpan(34, 2), (short)16);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BitConverter.TryWriteBytes(bytes.AsSpan(40, 4), pcm.Length);
        pcm.CopyTo(bytes, 44);
        return bytes;
    }
}
