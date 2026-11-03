using Microsoft.Xna.Framework;

namespace Jerichofall.View;

/// <summary>Authored pixel lettering and night sky for the title screen.</summary>
internal static class BrandingArt
{
    private static readonly Color Ink = new(5, 12, 26);
    private static readonly Color Stone = new(17, 40, 65);
    private static readonly Color Edge = new(31, 65, 91);
    private static readonly Color Blue = new(44, 115, 160);
    private static readonly Color Azure = new(87, 174, 211);
    private static readonly Color Ice = new(207, 234, 230);

    private static readonly Dictionary<char, string> Letters = new()
    {
        ['A']="00100/01010/10001/10001/11111/10001/10001",
        ['B']="11110/10001/10001/11110/10001/10001/11110",
        ['C']="00111/01000/10000/10000/10000/01000/00111",
        ['D']="11100/10010/10001/10001/10001/10010/11100",
        ['E']="11111/10000/10000/11110/10000/10000/11111",
        ['F']="11111/10000/10000/11110/10000/10000/10000",
        ['G']="00111/01000/10000/10111/10001/01001/00111",
        ['H']="10001/10001/10001/11111/10001/10001/10001",
        ['I']="111/010/010/010/010/010/111",
        ['J']="00111/00010/00010/00010/10010/10010/01100",
        ['K']="10001/10010/10100/11000/10100/10010/10001",
        ['L']="10000/10000/10000/10000/10000/10000/11111",
        ['M']="10001/11011/10101/10101/10001/10001/10001",
        ['N']="10001/11001/11001/10101/10011/10011/10001",
        ['O']="01110/10001/10001/10001/10001/10001/01110",
        ['P']="11110/10001/10001/11110/10000/10000/10000",
        ['Q']="01110/10001/10001/10001/10101/10010/01101",
        ['R']="11110/10001/10001/11110/10100/10010/10001",
        ['S']="01111/10000/10000/01110/00001/00001/11110",
        ['T']="11111/00100/00100/00100/00100/00100/00100",
        ['U']="10001/10001/10001/10001/10001/10001/01110",
        ['V']="10001/10001/10001/10001/10001/01010/00100",
        ['W']="1000001/1000001/1000001/1001001/1001001/1010101/0100010",
        ['X']="10001/10001/01010/00100/01010/10001/10001",
        ['Y']="10001/10001/01010/00100/00100/00100/00100",
        ['Z']="11111/00001/00010/00100/01000/10000/11111",
        ['0']="01110/10001/10011/10101/11001/10001/01110",
        ['1']="010/110/010/010/010/010/111",
        ['2']="01110/10001/00001/00010/00100/01000/11111",
        ['3']="11110/00001/00001/01110/00001/00001/11110",
        ['4']="00010/00110/01010/10010/11111/00010/00010",
        ['5']="11111/10000/10000/11110/00001/00001/11110",
        ['6']="00111/01000/10000/11110/10001/10001/01110",
        ['7']="11111/00001/00010/00100/00100/01000/01000",
        ['8']="01110/10001/10001/01110/10001/10001/01110",
        ['9']="01110/10001/10001/01111/00001/00010/11100",
        ['-']="000/000/000/111/000/000/000",
        ['!']="1/1/1/1/1/0/1"
    };

    public static Color[] Sky(int width, int height)
    {
        var c = new Canvas(width, height);
        Sky(c, width, height);
        return c.Pixels;
    }

    public static Color[] Logo(int width, int height)
    {
        var c = new Canvas(width, height);
        Text(c, "JERICHOFALL", 4, 4, width / 2, 27, false, true);
        return c.Pixels;
    }

    private static int TextWidth(string text) => text.Sum(ch => ch == ' ' ? 4 : Letters[ch].IndexOf('/') + 1) - 1;

    private static void Text(Canvas c, string text, int sx, int sy, int cx, int y, bool glow = false, bool carved = false)
    {
        sx = Math.Max(1, sx); sy = Math.Max(1, sy);
        var mask = new bool[c.Width * c.Height];
        var x = cx - TextWidth(text) * sx / 2;
        foreach (char ch in text)
        {
            if (ch == ' ') { x += 4 * sx; continue; }
            var rows = Letters[ch].Split('/');
            for (int row = 0; row < rows.Length; row++) for (int col = 0; col < rows[row].Length; col++)
                if (rows[row][col] == '1')
                    for (int yy = 0; yy < sy + (carved ? 1 : 0); yy++) for (int xx = 0; xx < sx + (carved ? 1 : 0); xx++)
                    {
                        int px = x + col * sx + xx, py = y + row * sy + yy;
                        if (c.Inside(px, py)) mask[py * c.Width + px] = true;
                    }
            x += (rows[0].Length + 1) * sx;
        }
        bool At(int px, int py) => c.Inside(px, py) && mask[py * c.Width + px];
        for (int py = 0; py < c.Height; py++) for (int px = 0; px < c.Width; px++)
        {
            if (glow)
            {
                // Low-alpha glints, not another opaque face over the letters.
                if (At(px, py) && !At(px, py - 1)) c.Set(px, py, Ice * 0.35f);
                else if (!At(px, py) && (At(px - 1, py) || At(px + 1, py) || At(px, py - 1) || At(px, py + 1))) c.Set(px, py, Azure * 0.12f);
            }
            else if (At(px, py))
            {
                var ink = !At(px, py - 1) ? Ice : !At(px + 1, py) ? Edge : py < y + sy * 3 ? new Color(119, 193, 218) : py < y + sy * 5 ? Azure : Blue;
                if (!carved) ink = !At(px, py - 1) ? Ice : py < y + 3 * sy ? Azure : Blue;
                c.Set(px, py, ink);
            }
            else if (At(px - 1, py) || At(px + 1, py) || At(px, py + 1) || At(px, py - 1) || At(px - 1, py - 2)) c.Set(px, py, Ink);
        }
    }

    private static void Sky(Canvas c, int fw, int fh)
    {
        for (int f = 0; f < c.Height / fh; f++)
        {
            int oy = f * fh;
            for (int y = 0; y < fh; y++) c.Rect(0, oy + y, fw, 1, y < 48 ? new Color(8, 17, 39) : y < 99 ? new Color(11, 25, 49) : new Color(14, 34, 57));
            for (int i = 0; i < 106; i++)
            {
                int x = (i * 73 + i * i * 7 + 17) % fw, y = (i * 41 + i * i * 3 + 11) % (fh - 9);
                c.Set(x, oy + y, (i + f) % 7 == 0 ? Azure : Edge);
                if ((i + f) % 19 == 0) { c.Set(x - 1, oy + y, Blue); c.Set(x + 1, oy + y, Blue); c.Set(x, oy + y - 1, Blue); c.Set(x, oy + y + 1, Blue); }
            }
            for (int x = -20; x < fw; x += 97)
            {
                int y = oy + 101 + (x / 97 % 3) * 11;
                c.Rect(x, y, 65, 1, Stone); c.Rect(x + 12, y - 1, 43, 1, Stone); c.Rect(x + 29, y + 2, 47, 1, Stone);
            }
        }
    }

    internal sealed class Canvas(int width, int height)
    {
        public readonly int Width = width, Height = height;
        public readonly Color[] Pixels = new Color[width * height];
        public bool Inside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public void Set(int x, int y, Color color) { if (Inside(x, y)) Pixels[y * Width + x] = color; }
        public void Rect(int x, int y, int w, int h, Color color) { for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++) Set(xx, yy, color); }
        public void Diamond(int cx, int cy, int rx, int ry, Color color) { for (int y = -ry; y <= ry; y++) { int span = rx * (ry - Math.Abs(y)) / Math.Max(1, ry); Rect(cx - span, cy + y, span * 2 + 1, 1, color); } }
        public void Line(int x0, int y0, int x1, int y1, Color color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
            while (true) { Set(x0, y0, color); if (x0 == x1 && y0 == y1) break; int twice = error * 2; if (twice >= dy) { error += dy; x0 += sx; } if (twice <= dx) { error += dx; y0 += sy; } }
        }
    }
}
