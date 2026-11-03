using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;


namespace Jerichofall.View;

/// <summary>Hand-authored pixel masks; no sampled or recolored game imagery.</summary>
internal static class RelicArt
{
    private static readonly Dictionary<Equipment, Texture2D> Items = [];
    private static readonly Dictionary<Equipment, Texture2D> Ammo = [];
    internal static Color Color(Equipment kind) => kind switch
    {
        Equipment.Stone=>new(211,212,193),Equipment.Staff=>new(185,126,64),Equipment.Jawbone=>new(237,227,183),Equipment.Spear=>new(229,186,98),
        Equipment.EmberVessel => new(240, 137, 58), Equipment.BronzeDisc => new(223, 179, 89),
        Equipment.StormJavelin => new(121, 210, 229), Equipment.Manna => new(240, 226, 173),
        Equipment.CovenantSeal => new(118, 171, 239), _ => new(156, 207, 164)
    };

    internal static Texture2D Item(Equipment kind)
    {
        if (Items.TryGetValue(kind, out var texture)) return texture;
        string[] mask = kind switch
        {
            Equipment.EmberVessel => ["00000110000", "00001310000", "00001210000", "00012210000", "00122221000", "01223322100", "01234322100", "01223222100", "00122221000", "00011110000"],
            Equipment.BronzeDisc => ["00011111000", "00123332100", "01222223210", "12211122321", "12210012321", "12210012321", "12211122321", "01222223210", "00122222100", "00011111000"],
            Equipment.StormJavelin => ["0000000000000100", "0000000000001230", "0000000000012331", "1111111111123331", "0222222222212331", "0000000000001230", "0000000000000100"],
            Equipment.Manna => ["00001111000", "00012222100", "00123323210", "01233233321", "01223323221", "01222222221", "00111111110", "00123332100", "00012221000", "00001110000"],
            Equipment.CovenantSeal => ["00001110000", "00012221000", "00122322100", "01223332210", "01231413210", "01231413210", "01223332210", "00122322100", "00012221000", "00001110000"],
            _ => ["01000000100", "12100001210", "12310012310", "12321123210", "01232123210", "00123123210", "00123223210", "00122222210", "00011111100", "00000000000"]
        };
        return Items[kind] = Make(mask, kind);
    }

    internal static Texture2D AmmoIcon(Equipment kind)
    {
        if (Ammo.TryGetValue(kind, out var texture)) return texture;
        string[] mask = kind switch
        {
            Equipment.Stone=>["000","010","232","121","000"],
            Equipment.Staff=>["011","120","020","020","010"],
            Equipment.Jawbone=>["001","032","121","020","010"],
            Equipment.Spear=>["030","232","010","020","010"],
            Equipment.EmberVessel => ["010", "121", "232", "121", "010"],
            Equipment.BronzeDisc => ["010", "232", "101", "232", "010"],
            _ => ["030", "232", "010", "020", "010"]
        };
        return Ammo[kind] = Make(mask, kind);
    }

    internal static void Dispose()
    {
        foreach(var texture in Items.Values.Concat(Ammo.Values))texture.Dispose();
        Items.Clear();Ammo.Clear();
    }

    private static Texture2D Make(string[] mask, Equipment kind)
    {
        var width = mask[0].Length;
        var ink = new Color(34, 27, 37);
        var mid = Color(kind);
        var light = Microsoft.Xna.Framework.Color.Lerp(mid, Microsoft.Xna.Framework.Color.White, .6f);
        var pixels = new Color[width * mask.Length];
        for (var y = 0; y < mask.Length; y++) for (var x = 0; x < width; x++)
            pixels[y * width + x] = mask[y][x] switch
            { '1' => ink, '2' => mid, '3' => light, '4' => new Color(238, 192, 90), _ => Microsoft.Xna.Framework.Color.Transparent };
        return Canvas2D.Texture(width, mask.Length, pixels);
    }

    internal static void Segment(Vector2 from, Vector2 to, Color tint)
    {
        var span = to - from;
        var steps = Math.Max(1, (int)MathF.Ceiling(Math.Max(Math.Abs(span.X), Math.Abs(span.Y))));
        for (var index = 0; index <= steps; index++)
        {
            var point = from + span * (index / (float)steps);
            Canvas2D.Rect(MathF.Round(point.X), MathF.Round(point.Y), 1, 1, tint);
        }
    }
}
