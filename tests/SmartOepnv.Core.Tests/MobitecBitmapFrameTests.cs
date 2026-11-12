using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public sealed class MobitecBitmapFrameTests
{
    [Fact]
    public void Enabled_fonts_include_the_zedit_destination_font()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var destination = Assert.Single(fonts, font => font.Id == 'i');
        Assert.Equal("13×9", destination.Label);
        Assert.Equal("7×8", Assert.Single(fonts, font => font.Id == 'd').Label);
        Assert.Equal(0x69, destination.WireCode);

        var grid = new bool[16, 32];
        destination.DrawTop(grid, "A", 0, 0);
        Assert.True(grid[0, 4]);
        Assert.False(grid[0, 0]);
    }

    [Fact]
    public void Pixel_roundtrip_keeps_the_16_rows_of_a_front_sign()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var pixels = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        pixels[0, 0] = true;
        pixels[5, 10] = true;
        pixels[14, 3] = true;
        pixels[15, 127] = true;

        var frame = MobitecBitmapFrame.Encode(pixels, MobitecBitmapFrame.FrontAddress, MobitecBitmapFrame.FrontWidth);
        Assert.NotNull(frame);
        Assert.Equal(0x06, frame![1]);
        Assert.Equal(128, frame[5]);

        var back = MobitecBitmapFrame.Decode(frame, MobitecBitmapFrame.FrontWidth, fonts);
        Assert.True(MobitecBitmapFrame.Same(pixels, back));
    }

    [Fact]
    public void Two_line_text_keeps_the_lower_line_on_the_sign()
    {
        var font = Assert.Single(MobitecSignFont.LoadEnabled(), item => item.Id == 'd');
        var lower = new bool[MobitecBitmapFrame.Height, 32];
        font.DrawBaseline(lower, "H"u8, 0, 0x11);
        Assert.True(lower[9, 0]);
        Assert.True(lower[15, 0]);
        Assert.False(lower[8, 0]);

        var upper = new bool[MobitecBitmapFrame.Height, 32];
        font.DrawBaseline(upper, "H"u8, 0, 0x08);
        Assert.True(upper[0, 0]);
        Assert.True(upper[6, 0]);
        Assert.False(upper[7, 0]);
    }

    [Fact]
    public void Front_line_number_is_top_aligned_and_the_line_sign_keeps_umlaut_room()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var large = Assert.Single(fonts, font => font.Id == 'i');
        var narrow = Assert.Single(fonts, font => font.Id == 'l');

        var front = new bool[MobitecBitmapFrame.Height, 32];
        large.DrawBaseline(front, "R"u8, 0, 0x0C);
        Assert.True(front[0, 0]);
        Assert.True(front[12, 0]);
        Assert.False(front[13, 0]);

        var line = new bool[MobitecBitmapFrame.Height, 32];
        large.DrawBaseline(line, "R"u8, 0, 0x0F);
        Assert.False(line[0, 0]);
        Assert.True(line[3, 0]);
        Assert.True(line[15, 0]);

        var centered = new bool[MobitecBitmapFrame.Height, 32];
        narrow.DrawBaseline(centered, "R"u8, 0, 0x0C);
        Assert.False(centered[2, 0]);
        Assert.True(centered[3, 0]);
        Assert.True(centered[12, 0]);
        Assert.False(centered[13, 0]);
    }

    [Fact]
    public void Empty_sign_does_not_produce_a_frame()
    {
        var pixels = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        Assert.Null(MobitecBitmapFrame.Encode(pixels, MobitecBitmapFrame.LineAddress, MobitecBitmapFrame.LineWidth));
    }

    [Fact]
    public void Wechseltext_pages_stay_apart_and_keep_their_interval()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var first = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        var second = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        first[0, 0] = true;
        second[0, 5] = true;

        var frame = MobitecBitmapFrame.EncodePages(
            [first, second],
            MobitecBitmapFrame.FrontAddress,
            MobitecBitmapFrame.FrontWidth,
            4);
        Assert.NotNull(frame);
        Assert.Equal(0xF1, frame![3]);

        var shown = MobitecBitmapFrame.Decode(frame, MobitecBitmapFrame.FrontWidth, fonts);
        Assert.True(shown[0, 0]);
        Assert.False(shown[0, 5]);

        var decoded = MobitecBitmapFrame.DecodeSign(frame, MobitecBitmapFrame.FrontWidth, fonts);
        Assert.Equal(2, decoded.Pages.Count);
        Assert.Equal(4, decoded.IntervalSeconds);
        Assert.True(decoded.Pages[0][0, 0]);
        Assert.False(decoded.Pages[0][0, 5]);
        Assert.False(decoded.Pages[1][0, 0]);
        Assert.True(decoded.Pages[1][0, 5]);

        var retimed = MobitecBitmapFrame.WithInterval(frame, 6);
        var again = MobitecBitmapFrame.DecodeSign(retimed, MobitecBitmapFrame.FrontWidth, fonts);
        Assert.Equal(6, again.IntervalSeconds);
        Assert.True(MobitecBitmapFrame.Same(decoded.Pages[0], again.Pages[0]));
        Assert.True(MobitecBitmapFrame.Same(decoded.Pages[1], again.Pages[1]));
    }

    [Fact]
    public void First_page_drops_the_line_wechsel()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var first = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        var second = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        first[0, 0] = true;
        second[0, 5] = true;

        var frame = MobitecBitmapFrame.EncodePages(
            [first, second],
            MobitecBitmapFrame.LineAddress,
            MobitecBitmapFrame.LineWidth,
            3);
        var single = MobitecBitmapFrame.FirstPage(frame!);
        Assert.Equal(0xF0, single[3]);
        var decoded = MobitecBitmapFrame.DecodeSign(single, MobitecBitmapFrame.LineWidth, fonts);
        Assert.Single(decoded.Pages);
        Assert.True(decoded.Pages[0][0, 0]);
        Assert.False(decoded.Pages[0][0, 5]);
    }

    [Fact]
    public void Plain_front_and_line_text_is_a_font_object_not_a_bitmap()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var lineFont = Assert.Single(fonts, font => font.Id == 'i');
        var destination = Assert.Single(fonts, font => font.Id == 'd');

        var line = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        lineFont.DrawTop(line, "S8", 1, 0);
        var lineFrame = MobitecBitmapFrame.EncodePreferringText(
            [line],
            MobitecBitmapFrame.LineAddress,
            MobitecBitmapFrame.LineWidth,
            3,
            fonts);
        Assert.NotNull(lineFrame);
        Assert.False(MobitecBitmapFrame.ContainsBitmap(lineFrame));
        Assert.True(HasTextObject(lineFrame!, 0x69, "S8"));
        Assert.True(MobitecBitmapFrame.Same(
            line,
            MobitecBitmapFrame.Decode(lineFrame, MobitecBitmapFrame.LineWidth, fonts)));

        var front = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        destination.DrawTop(front, "Ersatzverkehr", 0, 0);
        destination.DrawTop(front, "Neuss Hbf", 0, 9);
        var frontFrame = MobitecBitmapFrame.EncodePreferringText(
            [front],
            MobitecBitmapFrame.FrontAddress,
            MobitecBitmapFrame.FrontWidth,
            3,
            fonts);
        Assert.NotNull(frontFrame);
        Assert.False(MobitecBitmapFrame.ContainsBitmap(frontFrame));
        Assert.True(MobitecBitmapFrame.ContainsText(frontFrame));
        Assert.True(MobitecBitmapFrame.Same(
            front,
            MobitecBitmapFrame.Decode(frontFrame, MobitecBitmapFrame.FrontWidth, fonts)));
    }

    [Fact]
    public void A_logo_next_to_text_stays_a_bitmap_and_the_side_stays_a_bitmap()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var destination = Assert.Single(fonts, font => font.Id == 'd');
        var front = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        destination.DrawTop(front, "Grenze", 40, 4);
        front[0, 0] = true;
        front[1, 2] = true;
        front[2, 4] = true;
        front[3, 1] = true;
        front[4, 3] = true;
        front[5, 5] = true;

        var frame = MobitecBitmapFrame.EncodePreferringText(
            [front],
            MobitecBitmapFrame.FrontAddress,
            MobitecBitmapFrame.FrontWidth,
            3,
            fonts);
        Assert.NotNull(frame);
        Assert.True(MobitecBitmapFrame.ContainsBitmap(frame));
        Assert.True(MobitecBitmapFrame.ContainsText(frame));
        Assert.True(HasTextObject(frame!, 0x64, "Grenze"));
        Assert.True(MobitecBitmapFrame.Same(
            front,
            MobitecBitmapFrame.Decode(frame, MobitecBitmapFrame.FrontWidth, fonts)));

        var side = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.SideWidth];
        destination.DrawTop(side, "Niederkruechten", 0, 0);
        var sideFrame = MobitecBitmapFrame.EncodePreferringText(
            [side],
            MobitecBitmapFrame.SideAddress,
            MobitecBitmapFrame.SideWidth,
            3,
            fonts);
        Assert.NotNull(sideFrame);
        Assert.False(MobitecBitmapFrame.ContainsBitmap(sideFrame));
        Assert.True(MobitecBitmapFrame.ContainsText(sideFrame));
        Assert.Equal(["Text"], MobitecBitmapFrame.PageKinds(sideFrame));
        Assert.True(MobitecBitmapFrame.Same(
            side,
            MobitecBitmapFrame.Decode(sideFrame, MobitecBitmapFrame.SideWidth, fonts)));
    }

    [Fact]
    public void Front_wechsel_keeps_text_on_the_first_page_and_bitmap_on_the_second()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var destination = Assert.Single(fonts, font => font.Id == 'd');
        var text = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        destination.DrawTop(text, "S28", 0, 0);
        var graphic = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        graphic[1, 2] = true;
        graphic[2, 4] = true;
        graphic[3, 6] = true;
        graphic[4, 8] = true;

        var frame = MobitecBitmapFrame.EncodePreferringText(
            [text, graphic],
            MobitecBitmapFrame.FrontAddress,
            MobitecBitmapFrame.FrontWidth,
            3,
            fonts);

        Assert.NotNull(frame);
        Assert.Equal(0xF1, frame![3]);
        Assert.Equal(["Text", "Grafik"], MobitecBitmapFrame.PageKinds(frame));
    }

    [Fact]
    public void Letter_shape_inside_a_graphic_stays_bitmap()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var font = Assert.Single(fonts, item => item.Id == 'd');
        var graphic = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        var origin = 8;
        font.DrawTop(graphic, "I", origin, 4);
        var end = origin + font.MeasureWidth("I");
        var cols = graphic.GetLength(1);
        for (var row = 0; row < graphic.GetLength(0); row++)
        {
            var ink = false;
            for (var column = origin; column < end && column < cols; column++)
            {
                ink |= graphic[row, column];
            }

            if (!ink)
            {
                continue;
            }

            graphic[row, origin - 2] = true;
            graphic[row, origin - 1] = true;
            if (end - 1 >= 0 && end - 1 < cols)
            {
                graphic[row, end - 1] = true;
            }

            if (end < cols)
            {
                graphic[row, end] = true;
            }
        }

        var frame = MobitecBitmapFrame.EncodePreferringText(
            [graphic],
            MobitecBitmapFrame.LineAddress,
            MobitecBitmapFrame.LineWidth,
            3,
            fonts);

        Assert.NotNull(frame);
        Assert.Equal(["Grafik"], MobitecBitmapFrame.PageKinds(frame));
        Assert.False(MobitecBitmapFrame.ContainsText(frame));
    }

    [Fact]
    public void Hand_drawn_line_number_stays_a_bitmap_even_if_a_letter_matches()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var lineFont = Assert.Single(fonts, font => font.Id == 'l');
        var destination = Assert.Single(fonts, font => font.Id == 'd');

        var line = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        lineFont.DrawTop(line, "RE", 0, 1);
        line[2, 20] = true;
        line[3, 21] = true;
        line[4, 22] = true;
        line[5, 23] = true;
        line[6, 24] = true;

        var lineFrame = MobitecBitmapFrame.EncodePreferringText(
            [line],
            MobitecBitmapFrame.LineAddress,
            MobitecBitmapFrame.LineWidth,
            3,
            fonts);
        Assert.NotNull(lineFrame);
        Assert.Equal(["Grafik"], MobitecBitmapFrame.PageKinds(lineFrame));
        Assert.False(MobitecBitmapFrame.ContainsText(lineFrame));
        Assert.True(MobitecBitmapFrame.Same(
            line,
            MobitecBitmapFrame.Decode(lineFrame, MobitecBitmapFrame.LineWidth, fonts)));

        var front = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        for (var row = 0; row < line.GetLength(0); row++)
        {
            for (var column = 0; column < line.GetLength(1); column++)
            {
                front[row, column] = line[row, column];
            }
        }

        destination.DrawTop(front, "Ziel", 40, 0);
        var frontFrame = MobitecBitmapFrame.EncodePreferringText(
            [front],
            MobitecBitmapFrame.FrontAddress,
            MobitecBitmapFrame.FrontWidth,
            3,
            fonts);
        Assert.NotNull(frontFrame);
        Assert.Equal(["Text+Grafik"], MobitecBitmapFrame.PageKinds(frontFrame));
        Assert.True(MobitecBitmapFrame.ContainsBitmap(frontFrame));
        Assert.True(MobitecBitmapFrame.Same(
            front,
            MobitecBitmapFrame.Decode(frontFrame, MobitecBitmapFrame.FrontWidth, fonts)));
    }

    [Fact]
    public void Side_text_and_uuml_dots_survive_saving()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var font = Assert.Single(fonts, item => item.Id == 'd');
        var side = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.SideWidth];
        font.DrawTop(side, "Ersatzverkehr", 15, 0);
        font.DrawTop(side, "Düsseldorf", 14, 8);

        var frame = MobitecBitmapFrame.EncodePreferringText(
            [side],
            MobitecBitmapFrame.SideAddress,
            MobitecBitmapFrame.SideWidth,
            3,
            fonts);

        Assert.NotNull(frame);
        Assert.True(HasTextObject(frame!, 0x64, "Ersatzverkehr"));
        Assert.DoesNotContain((byte)125, frame!);
        Assert.True(MobitecBitmapFrame.Same(
            side,
            MobitecBitmapFrame.Decode(frame, MobitecBitmapFrame.SideWidth, fonts)));
    }

    [Fact]
    public void S28_line_symbol_is_not_read_as_text()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var stored = Convert.FromBase64String(
            "/wui8dAc0RDSANMO1GlTMjiwA9IQ0wXUdmlp0hLTBtRmadIO0wnUcknSD9MJ1HIuLi4gSdIN0wrUZUnSF9MK1GVJ0gPTC9RySSAh0gnTDdRmSdIF0xHUZUlJ0gTTBNR3MDAwMDAgICAwICYiICAgICAiJiAw0gTTCdR3MiIjIjIgICAjICAgJCAwICQwICAj0gPTDtR3JC0gLCAtICAgICAgICEhISEpLD4sKNID0xPUdyEhICEgISEgICAhISEhISEhISHH/w==");
        var sign = MobitecBitmapFrame.DecodeSign(stored, MobitecBitmapFrame.LineWidth, fonts);
        var frame = MobitecBitmapFrame.EncodePreferringText(
            sign.Pages,
            MobitecBitmapFrame.LineAddress,
            MobitecBitmapFrame.LineWidth,
            sign.IntervalSeconds,
            fonts);

        Assert.NotNull(frame);
        Assert.Equal(["Text", "Grafik"], MobitecBitmapFrame.PageKinds(frame));
        Assert.True(HasTextObject(frame!, 0x69, "S28"));
        Assert.True(MobitecBitmapFrame.Same(
            sign.Pages[1],
            MobitecBitmapFrame.DecodeSign(frame, MobitecBitmapFrame.LineWidth, fonts).Pages[1]));
    }

    private static bool HasTextObject(byte[] frame, byte font, string text)
    {
        var wire = System.Text.Encoding.ASCII.GetBytes(text);
        for (var i = 0; i + 2 + wire.Length <= frame.Length; i++)
        {
            if (frame[i] != 0xD4 || frame[i + 1] != font)
            {
                continue;
            }

            var matches = true;
            for (var j = 0; j < wire.Length; j++)
            {
                if (frame[i + 2 + j] != wire[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Destination_sign_encodes_as_text_quickly()
    {
        var fonts = MobitecSignFont.LoadEnabled();
        var destination = Assert.Single(fonts, font => font.Id == 'd');
        var lineFont = Assert.Single(fonts, font => font.Id == 'i');

        var front = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.FrontWidth];
        destination.DrawTop(front, "Düsseldorf Hbf", 2, 0);
        destination.DrawTop(front, "über Solingen", 2, 8);

        var side = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.SideWidth];
        destination.DrawTop(side, "Düsseldorf Hbf", 2, 0);
        destination.DrawTop(side, "über Solingen", 2, 8);

        var line = new bool[MobitecBitmapFrame.Height, MobitecBitmapFrame.LineWidth];
        lineFont.DrawTop(line, "RE47", 0, 3);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var frontFrame = MobitecBitmapFrame.EncodePreferringText(
            [front], MobitecBitmapFrame.FrontAddress, MobitecBitmapFrame.FrontWidth, 5, fonts);
        var sideFrame = MobitecBitmapFrame.EncodePreferringText(
            [side], MobitecBitmapFrame.SideAddress, MobitecBitmapFrame.SideWidth, 5, fonts);
        var lineFrame = MobitecBitmapFrame.EncodePreferringText(
            [line], MobitecBitmapFrame.LineAddress, MobitecBitmapFrame.LineWidth, 5, fonts);
        watch.Stop();

        Assert.NotNull(frontFrame);
        Assert.NotNull(sideFrame);
        Assert.NotNull(lineFrame);
        Assert.True(watch.ElapsedMilliseconds < 1500, $"encode took {watch.ElapsedMilliseconds} ms");
    }
}
