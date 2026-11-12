using System.IO.Compression;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Schreibt die Pixelgrafik als PNG, damit sie im Ordner und in der Liste als Bild sichtbar ist.
/// Ein Anzeigepunkt wird ein 8×8-Block in Bernstein auf dunklem Grund.
/// </summary>
internal static class MobitecGraphicPng
{
    public const int Scale = 8;

    public static void Write(string path, int width, int height, IReadOnlyList<(int X, int Y)> points)
    {
        var lit = new bool[height, width];
        foreach (var (x, y) in points)
        {
            lit[y, x] = true;
        }

        var pixelWidth = width * Scale;
        var pixelHeight = height * Scale;
        var rowBytes = 1 + pixelWidth * 4;
        var scanlines = new byte[pixelHeight * rowBytes];
        var index = 0;
        for (var y = 0; y < pixelHeight; y++)
        {
            scanlines[index++] = 0;
            var cellY = y / Scale;
            var gapY = y % Scale == Scale - 1;
            for (var x = 0; x < pixelWidth; x++)
            {
                var on = lit[cellY, x / Scale] && !gapY && x % Scale != Scale - 1;
                if (on)
                {
                    scanlines[index++] = 0xFF;
                    scanlines[index++] = 0xC1;
                    scanlines[index++] = 0x07;
                    scanlines[index++] = 0xFF;
                }
                else
                {
                    scanlines[index++] = 0x18;
                    scanlines[index++] = 0x18;
                    scanlines[index++] = 0x18;
                    scanlines[index++] = 0xFF;
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, Encode(pixelWidth, pixelHeight, scanlines));
        File.Move(temp, path, overwrite: true);
    }

    private static byte[] Encode(int width, int height, byte[] scanlines)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(output, "IHDR"u8, ihdr);
        Chunk(output, "IDAT"u8, Zlib(scanlines));
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using var compressed = new MemoryStream();
        compressed.WriteByte(0x78);
        compressed.WriteByte(0x9C);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }

        var sum = Adler32(data);
        compressed.WriteByte((byte)(sum >> 24));
        compressed.WriteByte((byte)(sum >> 16));
        compressed.WriteByte((byte)(sum >> 8));
        compressed.WriteByte((byte)sum);
        return compressed.ToArray();
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteInt(length, 0, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        WriteInt(crc, 0, Crc(type, data));
        output.Write(crc);
    }

    private static int Crc(ReadOnlySpan<byte> type, byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var value in type)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return (int)(crc ^ 0xFFFFFFFF);
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static void WriteInt(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
