using System.IO.Compression;
using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public sealed class MobitecGraphicLibraryTests
{
    [Fact]
    public void Saved_graphic_comes_back_with_the_same_pixels()
    {
        var folder = NewFolder();
        var points = new List<(int X, int Y)> { (1, 0), (2, 15), (27, 8) };
        MobitecGraphicLibrary.Save(folder, "S28 Logo", 28, 16, points);

        var loaded = Assert.Single(MobitecGraphicLibrary.List(folder));
        Assert.Equal("S28 Logo", loaded.Name);
        Assert.Equal(28, loaded.Width);
        Assert.Equal(16, loaded.Height);
        Assert.Equal(points, loaded.Points);
        Assert.True(MobitecGraphicLibrary.Exists(folder, "S28 Logo"));
        var png = File.ReadAllBytes(MobitecGraphicLibrary.ImagePath(folder, "S28 Logo"));
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png.Take(4));
        Assert.Equal(28 * 8, ReadInt(png, 16));
        Assert.Equal(16 * 8, ReadInt(png, 20));
        var pixels = InflateIdat(png);
        var rowBytes = 1 + 28 * 8 * 4;
        var lit = pixels[8 * 4 + 1];
        Assert.Equal(0xFF, lit);
        Assert.Equal(0xC1, pixels[8 * 4 + 2]);
        Assert.Equal(0x18, pixels[rowBytes + 0 * 4 + 1]);
    }

    [Fact]
    public void Saving_again_replaces_the_graphic()
    {
        var folder = NewFolder();
        MobitecGraphicLibrary.Save(folder, "Logo", 4, 4, [(0, 0)]);
        MobitecGraphicLibrary.Save(folder, "Logo", 4, 4, [(3, 3)]);

        var loaded = Assert.Single(MobitecGraphicLibrary.List(folder));
        Assert.Equal([(3, 3)], loaded.Points);
    }

    [Fact]
    public void Invalid_name_is_rejected()
    {
        Assert.False(MobitecGraphicLibrary.IsValidName("S28/Logo"));
        Assert.False(MobitecGraphicLibrary.IsValidName(" "));
        Assert.False(MobitecGraphicLibrary.IsValidName(new string('a', 81)));
        var folder = NewFolder();
        Assert.Throws<ArgumentException>(() =>
            MobitecGraphicLibrary.Save(folder, "S28/Logo", 4, 4, [(0, 0)]));
        Assert.Empty(MobitecGraphicLibrary.List(folder));
    }

    [Fact]
    public void Delete_removes_only_the_named_graphic()
    {
        var folder = NewFolder();
        MobitecGraphicLibrary.Save(folder, "Eins", 2, 2, [(0, 0)]);
        MobitecGraphicLibrary.Save(folder, "Zwei", 2, 2, [(1, 1)]);

        Assert.True(MobitecGraphicLibrary.Delete(folder, "Eins"));
        Assert.False(File.Exists(MobitecGraphicLibrary.ImagePath(folder, "Eins")));
        var left = Assert.Single(MobitecGraphicLibrary.List(folder));
        Assert.Equal("Zwei", left.Name);
        Assert.True(File.Exists(MobitecGraphicLibrary.ImagePath(folder, "Zwei")));
    }

    [Fact]
    public void Broken_file_is_skipped()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "kaputt.json"), "{");
        MobitecGraphicLibrary.Save(folder, "Ganz", 2, 2, [(0, 1)]);

        var loaded = Assert.Single(MobitecGraphicLibrary.List(folder));
        Assert.Equal("Ganz", loaded.Name);
    }

    private static string NewFolder() =>
        Path.Combine(Path.GetTempPath(), "mobitec-grafik-" + Guid.NewGuid().ToString("N"));

    private static int ReadInt(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static byte[] InflateIdat(byte[] png)
    {
        var offset = 8;
        while (offset + 8 < png.Length)
        {
            var length = ReadInt(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                using var input = new MemoryStream(png, offset + 8 + 2, length - 6);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }

            offset += 12 + length;
        }

        throw new InvalidDataException("IDAT fehlt.");
    }
}
