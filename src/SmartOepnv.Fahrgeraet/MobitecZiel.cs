namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Mobitec-Ziele wie in der Tablet-App: Sonderziel, SEV-Marke, Wechseltext und Linie.
/// </summary>
public static class MobitecZiel
{
    public static IEnumerable<byte[]> Frames(string line, string destination)
    {
        var goals = Goals(destination);
        var special = SpecialLetter(destination);
        if (special is not null && goals.Count <= 1)
        {
            var name = goals.Count == 0 ? destination : First(goals[0]);
            yield return Special(0x06, 0x80, special, name, front: true);
            yield return Special(0x07, 0x7E, special, name, front: false);
            yield return Line(special);
            yield break;
        }

        if (goals.Count >= 2)
        {
            yield return Line(line);
            yield return Sequence(0x07, 0x7E, goals.Select(goal => SideObjects(goal.A, goal.B)).ToList(), 3);
            yield return Sequence(0x06, 0x80, goals.Select(goal => FrontCycle(line, goal.A, goal.B)).ToList(), 3);
            yield break;
        }

        var pair = goals.Count == 0 ? new Goal(destination, "") : goals[0];
        yield return Front(line, pair.A, pair.B);
        yield return Side(pair.A, pair.B);
        yield return Line(line);
    }

    private static byte[] Front(string line, string top, string bottom)
    {
        var body = Head(0x06, 0x80, 0xF0);
        var trimmed = line.Trim();
        if (top.Length is >= 1 and <= 4 && bottom.Length >= 12)
        {
            Text(body, 0x00, 0x0F, 0x69, trimmed);
            Text(body, 0x1C, 0x08, 0x64, "   ");
            Text(body, 0x28, 0x0F, 0x69, "    ");
            Text(body, 0x40, 0x08, 0x64, top);
            Text(body, 0x1E, 0x0F, 0x64, bottom);
            return Sum(body);
        }

        FrontCycle(line, top, bottom).ForEach(obj => Text(body, obj.X, obj.Y, obj.Font, obj.Text));
        return Sum(body);
    }

    private static byte[] Side(string top, string bottom)
    {
        var body = Head(0x07, 0x7E, 0xF0);
        foreach (var obj in SideObjects(top, bottom))
        {
            Text(body, obj.X, obj.Y, obj.Font, obj.Text);
        }

        return Sum(body);
    }

    private static byte[] Special(byte address, byte d0, string letter, string name, bool front)
    {
        var body = Head(address, d0, 0xF0);
        if (front)
        {
            var x = letter.Length == 0
                ? Center(name, 0x69, 128, 0)
                : Center(name, 0x69, 100, 28);
            if (letter.Length > 0)
            {
                Text(body, Center(letter, letter.Length >= 4 ? 0x6C : 0x69, 28, 0), letter.Length >= 4 ? 0x0C : 0x0F, letter.Length >= 4 ? 0x6C : 0x69, letter);
            }

            Text(body, x, 0x0F, 0x69, name);
        }
        else
        {
            Text(body, Center(name, 0x69, 120, 0), 0x0F, 0x69, name);
        }

        return Sum(body);
    }

    private static byte[] Line(string line) => Sum(LineFrame(line));

    private static List<byte> LineFrame(string line)
    {
        var body = Head(0x0B, 0x1C, 0xF0);
        foreach (var obj in LineObjects(line))
        {
            Text(body, obj.X, obj.Y, obj.Font, obj.Text);
        }

        return body;
    }

    private static List<Piece> LineObjects(string line)
    {
        var text = line.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var four = text.Length >= 4;
        var font = four ? 0x6C : 0x69;
        var x = Math.Max(1, Center(text, font, 28, 0));
        return [new Piece(x, four ? 0x0C : 0x0F, font, text)];
    }

    private static List<Piece> FrontCycle(string line, string top, string bottom)
    {
        var objects = new List<Piece>();
        var trimmed = line.Trim();
        if (trimmed.Length >= 4)
        {
            objects.Add(new Piece(0, 0x0C, 0x6C, string.IsNullOrWhiteSpace(top) ? trimmed : trimmed + " "));
            var text = string.Join(' ', new[] { top, bottom }.Where(part => part.Length > 0));
            if (text.Length > 0)
            {
                objects.Add(new Piece(Center(text, 0x69, 100, 28), 0x0F, 0x69, text));
            }

            return objects;
        }

        if (top.Length is >= 1 and <= 4 && bottom.Length >= 12)
        {
            objects.Add(new Piece(0, 0x0F, 0x69, trimmed));
            objects.Add(new Piece(0x1C, 0x08, 0x64, "   "));
            objects.Add(new Piece(0x28, 0x0F, 0x69, "    "));
            objects.Add(new Piece(0x40, 0x08, 0x64, top));
            objects.Add(new Piece(0x1E, 0x0F, 0x64, bottom));
            return objects;
        }

        if (trimmed.Length > 0)
        {
            objects.Add(new Piece(0, 0x0F, 0x69, trimmed.Length == 1 ? trimmed + "  " : trimmed));
        }

        var hasLine = trimmed.Length > 0;
        if (top.Length > 0 && bottom.Length > 0)
        {
            objects.Add(new Piece(Center(top, 0x64, hasLine ? 100 : 128, hasLine ? 28 : 0), 0x08, 0x64, top));
            objects.Add(new Piece(Center(bottom, 0x64, hasLine ? 100 : 128, hasLine ? 28 : 0), 0x11, 0x64, bottom));
        }
        else if (top.Length > 0)
        {
            objects.Add(new Piece(
                trimmed.Length == 0 ? Center(top, 0x69, 128, 0) : Center(top, 0x69, 100, 28),
                0x0F,
                0x69,
                top));
        }

        return objects;
    }

    private static List<Piece> SideObjects(string top, string bottom)
    {
        if (top.Length > 0 && bottom.Length > 0)
        {
            return
            [
                new Piece(Center(top, 0x64, 120, 0), 0x08, 0x64, top),
                new Piece(Center(bottom, 0x64, 120, 0), 0x11, 0x64, bottom)
            ];
        }

        var text = top.Length > 0 ? top : bottom;
        return [new Piece(Center(text, 0x69, 120, 0), 0x0F, 0x69, text)];
    }

    private static byte[] Sequence(byte address, byte d0, List<List<Piece>> steps, int seconds)
    {
        var body = Head(address, d0, 0xF1);
        var takt = Math.Clamp(seconds, 1, 99);
        for (var i = 0; i < steps.Count; i++)
        {
            foreach (var obj in steps[i])
            {
                Text(body, obj.X, obj.Y, obj.Font, obj.Text);
            }

            if (i < steps.Count - 1 || steps.Count == 1)
            {
                body.Add(0xB0);
                body.Add((byte)takt);
            }
        }

        return Sum(body);
    }

    private static List<Goal> Goals(string destination)
    {
        var lines = destination.Replace("\r", "").Split('\n');
        var goals = new List<Goal>();
        for (var i = 0; i < lines.Length;)
        {
            var first = lines[i].Trim();
            if (i + 1 < lines.Length)
            {
                goals.Add(new Goal(first, lines[i + 1].Trim()));
                i += 2;
            }
            else
            {
                if (first.Length > 0)
                {
                    goals.Add(new Goal(first, ""));
                }

                break;
            }
        }

        return goals.Where(goal => goal.A.Length > 0 || goal.B.Length > 0).ToList();
    }

    private static string? SpecialLetter(string name)
    {
        if (name.Contains("Pause", StringComparison.OrdinalIgnoreCase)) return "P";
        if (name.Contains("Sonderfahrt", StringComparison.OrdinalIgnoreCase)) return "S";
        if (name.Contains("Betriebsfahrt", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Verkehrsbetr", StringComparison.OrdinalIgnoreCase)) return "D";
        if (name.Contains("Werkstatt", StringComparison.OrdinalIgnoreCase)) return "W";
        if (name.Contains("Schulbus", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Schubus", StringComparison.OrdinalIgnoreCase)) return "S";
        if (name.Contains("Ersatzverkehr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SEV", StringComparison.OrdinalIgnoreCase)) return "E";
        return null;
    }

    private static string First(Goal goal) => goal.A.Length > 0 ? goal.A : goal.B;

    private static int Center(string text, int font, int width, int origin)
    {
        var used = MobitecUmlaut.EstimateWidth(text, font);
        var slack = Math.Max(0, width - used);
        return Math.Clamp(origin + slack / 2, 0, 127);
    }

    private static List<byte> Head(byte address, byte d0, byte mode) =>
        [0xFF, address, 0xA2, mode, 0xD0, d0, 0xD1, 0x10];

    private static void Text(List<byte> body, int x, int y, int font, string text) =>
        MobitecUmlaut.Append(body, x, y, font, text);

    private static byte[] Sum(List<byte> payload)
    {
        var sum = 0;
        for (var i = 1; i < payload.Count; i++)
        {
            sum = (sum + payload[i]) & 0xFF;
        }

        if (sum is 0xFE or 0xFF)
        {
            payload.Add(0xFE);
            payload.Add((byte)(sum == 0xFE ? 0 : 1));
        }
        else
        {
            payload.Add((byte)sum);
        }

        payload.Add(0xFF);
        return payload.ToArray();
    }

    private readonly record struct Goal(string A, string B);
    private readonly record struct Piece(int X, int Y, int Font, string Text);
}
