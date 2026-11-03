using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;


namespace Jerichofall.View;

/// <summary>Original native-pixel landmarks. Each chapter keeps its silhouette in every progress state.</summary>
internal sealed class CheckpointIcons : IDisposable
{
    private const int GlyphSize = 13;
    private const int TextureSize = GlyphSize + 2;
    private static readonly Color Outline = new(31, 20, 44);
    private static readonly Color Gold = new(250, 214, 126);
    private static readonly Color Pale = new(255, 245, 206);
    private static readonly string[] GlyphIds =
    [
        "rameses", "succoth", "sea-crossing", "marah", "elim", "rephidim", "sinai", "paran", "kadesh",
        "moab", "jordan", "jericho", "dophkah", "wilderness-camps", "ezion-geber", "edom", "nebo", "ai"
    ];
    private static readonly Dictionary<string, int> GlyphIndices = GlyphIds
        .Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
    private readonly Texture2D[] landmarks;
    private readonly Texture2D[] lockedLandmarks;

    public CheckpointIcons(GraphicsDevice device)
    {
        landmarks = Glyphs.Select(glyph => CreateTexture(device, glyph, false)).ToArray();
        lockedLandmarks = Glyphs.Select(glyph => CreateTexture(device, glyph, true)).ToArray();
    }

    public void Draw(string chapterId, int x, int y, bool unlocked, bool completed, bool selected, int age)
    {
        var index = GlyphIndices[chapterId];
        // A tiny ground shadow separates the landmark from the detailed overworld without a tile or box.
        Canvas2D.Rect(x - 5, y + 6, 11, 3, Outline * .55f);
        Canvas2D.Rect(x - 7, y + 7, 15, 1, Outline * .4f);
        Canvas2D.SpriteBatch.Draw(unlocked ? landmarks[index] : lockedLandmarks[index], new Vector2(x - 7, y - 7), Color.White);
        // Jericho sits immediately west of the Jordan; keep its badge on the western side of the wall.
        var badgeX = chapterId == "jericho" ? x - 11 : x + 4;
        if (completed) DrawCompletedSeal(badgeX, y + 3);
        else if (!unlocked) DrawLock(badgeX, y + 3);
        else
        {
            // The next reachable checkpoint remains marked when the player browses another chapter.
            Canvas2D.Rect(x - 3, y + 8, 7, 3, Outline);
            Canvas2D.Rect(x - 2, y + 9, 5, 1, Gold);
            Canvas2D.Rect(x, y + 10, 1, 1, Gold);
        }
        if (selected)
        {
            var bob = age / 18 % 2;
            var tip = y - 10 - bob;
            // A small gold pennant points to the chosen landmark, including closely spaced river chapters.
            Canvas2D.Rect(x - 4, tip - 5, 9, 3, Outline);
            Canvas2D.Rect(x - 3, tip - 2, 7, 2, Outline);
            Canvas2D.Rect(x - 2, tip, 5, 1, Outline);
            Canvas2D.Rect(x - 1, tip + 1, 3, 1, Outline);
            Canvas2D.Rect(x, tip + 2, 1, 1, Outline);
            Canvas2D.Rect(x - 3, tip - 4, 7, 1, Pale);
            Canvas2D.Rect(x - 2, tip - 3, 5, 2, Gold);
            Canvas2D.Rect(x - 1, tip - 1, 3, 1, Gold);
            Canvas2D.Rect(x, tip, 1, 1, Pale);
        }
    }

    public void DrawPreview(string chapterId, int x, int y, bool unlocked)
    {
        var index = GlyphIndices[chapterId];
        Canvas2D.SpriteBatch.Draw(unlocked ? landmarks[index] : lockedLandmarks[index],
            new Rectangle(x - 15, y - 15, TextureSize * 2, TextureSize * 2), Color.White);
    }

    private static void DrawCompletedSeal(int x, int y)
    {
        Canvas2D.Rect(x + 1, y, 6, 7, Outline);
        Canvas2D.Rect(x, y + 1, 8, 5, Outline);
        Canvas2D.Rect(x + 1, y + 1, 6, 5, new Color(46, 86, 67));
        Canvas2D.Rect(x + 2, y + 3, 1, 2, Pale);
        Canvas2D.Rect(x + 3, y + 4, 1, 1, Pale);
        Canvas2D.Rect(x + 4, y + 3, 1, 1, Pale);
        Canvas2D.Rect(x + 5, y + 2, 1, 1, Pale);
    }

    private static void DrawLock(int x, int y)
    {
        Canvas2D.Rect(x + 1, y, 5, 4, Outline);
        Canvas2D.Rect(x + 2, y + 1, 3, 2, new Color(182, 169, 148));
        Canvas2D.Rect(x + 3, y + 2, 1, 1, Outline);
        Canvas2D.Rect(x, y + 3, 7, 5, Outline);
        Canvas2D.Rect(x + 1, y + 4, 5, 3, new Color(151, 139, 124));
        Canvas2D.Rect(x + 3, y + 5, 1, 1, Outline);
    }

    private static Texture2D CreateTexture(GraphicsDevice device, string[] glyph, bool locked)
    {
        if (glyph.Length != GlyphSize || glyph.Any(row => row.Length != GlyphSize))
            throw new InvalidDataException("Checkpoint glyphs must be exactly thirteen by thirteen native pixels.");
        var pixels = new Color[TextureSize * TextureSize];
        for (var y = 0; y < GlyphSize; y++)
        for (var x = 0; x < GlyphSize; x++)
        {
            if (glyph[y][x] == '.') continue;
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++) pixels[(y + 1 + oy) * TextureSize + x + 1 + ox] = Outline;
        }
        for (var y = 0; y < GlyphSize; y++)
        for (var x = 0; x < GlyphSize; x++)
        {
            if (glyph[y][x] == '.') continue;
            var color = Palette(glyph[y][x]);
            // Retain each landmark's regional color and internal detail when its route is still locked.
            pixels[(y + 1) * TextureSize + x + 1] = locked
                ? Color.Lerp(color, new Color(71, 64, 77), .48f) : color;
        }
        var texture = new Texture2D(device, TextureSize, TextureSize);
        texture.SetData(pixels);
        return texture;
    }

    private static Color Palette(char pixel) => pixel switch
    {
        'k' => Outline,
        'S' => new(247, 222, 168), 's' => new(202, 158, 104), 'd' => new(129, 83, 64),
        'G' => Gold, 'g' => new(202, 145, 66),
        'W' => new(231, 248, 232), 'B' => new(89, 184, 213), 'b' => new(47, 102, 160),
        'T' => new(123, 188, 112), 't' => new(53, 111, 86),
        'F' => new(235, 96, 63), 'f' => new(250, 161, 61),
        'p' => new(111, 83, 112),
        _ => throw new InvalidDataException("Unknown checkpoint pixel: " + pixel)
    };

    public void Dispose()
    {
        foreach (var texture in landmarks) texture.Dispose();
        foreach (var texture in lockedLandmarks) texture.Dispose();
    }

    private static readonly string[][] Glyphs =
    [
        // Rameses: sandstone pylon gate with lapis lintel and open central passage.
        [
            ".GG.......GG.",
            ".SS.......SS.",
            ".SSSSSSSSSSS.",
            ".SBBBBBBBBBS.",
            ".SgSSSSSSSgS.",
            ".SgS.....SgS.",
            ".SgS.....SgS.",
            ".SgS.....SgS.",
            ".SgS.....SgS.",
            ".SgS.....SgS.",
            ".SgS.....SgS.",
            ".SSS.....SSS.",
            "SSSSSSSSSSSSS"
        ],
        // Succoth: two linen tents and a small watch fire.
        [
            "..G......G...",
            "..s......s...",
            "..W......W...",
            ".WWW....WWW..",
            ".WsW...WWsWW.",
            "WWksW.WWsdsWW",
            "WWksWWWWsdsWW",
            "WWkkWWWWkksWW",
            "ddddddddddddd",
            "......F......",
            ".....fG......",
            ".....gFG.....",
            "....ddddd...."
        ],
        // Sea crossing: foaming water walls framing a gold dry path.
        [
            ".WW.......WW.",
            "WWBW.....WBWW",
            "WBBW.....WBBW",
            "WBbW.....WbBW",
            "WBbW.....WbBW",
            "WBbWG...GWbBW",
            "WBbWgs.sgWbBW",
            "WBbWgsssgWbBW",
            "WBbWgsssgWbBW",
            "WBbWgsssgWbBW",
            "WBBWgsssgWBBW",
            "WWWWGsssGWWWW",
            ".WWWGsssGWWW."
        ],
        // Marah: stone well, rope and green bitter water.
        [
            "..ddddddddd..",
            "..d...G...d..",
            "..d...g...d..",
            "..d...g...d..",
            "..d..ggg..d..",
            "..d.......d..",
            ".SSSSSSSSSSS.",
            ".SsTTTTTTsSS.",
            ".SSsttttsSSS.",
            "..SsssssssS..",
            "..SsSsssSsS..",
            "..SsssssssS..",
            "...SSSSSSS..."
        ],
        // Elim: spreading palms, blue spring and a sand rim.
        [
            "..TT...TT....",
            ".TTtTTTTtT...",
            "TTTTtTTTTtT..",
            "T..TdT..T.d..",
            "...ds.....d..",
            "...ds.....d..",
            "...ds.....d..",
            "...ds.....d..",
            "..SSSSSSSSS..",
            ".SSBBBBBBBSS.",
            "SSBWWWWWBbbSS",
            ".SSbbbbbbbSS.",
            "..SSSSSSSSS.."
        ],
        // Rephidim: split rock, water running through the cleft.
        [
            "....SS.SS....",
            "...SSs.SsS...",
            "..SSss.SssS..",
            ".SSsss.SsssS.",
            ".SSsssBssssS.",
            "SSsssdBsdsssS",
            "SSssddWsdsssS",
            "SSsdddWsdsssS",
            "SssdddWdddssS",
            "SssdddBWddssS",
            ".SddddBWddsS.",
            ".SSdddBWWdSS.",
            "..SSBBWBBBSS."
        ],
        // Sinai: fire-tipped summit and two inscribed covenant tablets.
        [
            "......F......",
            ".....fGF.....",
            ".....WSW.....",
            "....SSSSS....",
            "...SSdSdSS...",
            "..SSddSddSS..",
            ".SSdddddSdSS.",
            "SSdWWW.WWWdSS",
            "SdWSSSWSSSWdS",
            "..WSdSWSSdW..",
            "..WSdSWSSdW..",
            "..WSdSWSSdW..",
            "..WWWWWWWWW.."
        ],
        // Paran: a pillar of fire over the traveling camp.
        [
            "......F......",
            ".....FF......",
            ".....FfF.....",
            "....FfGF.....",
            "....FfGF.....",
            "....FfGfF....",
            "...FfGGfF....",
            "...FfGWfF....",
            "....FfGfF....",
            ".....fff.....",
            "..WWW...WWW..",
            ".WWsWW.WWsWW.",
            "WWWkWWWWWkWWW"
        ],
        // Kadesh: a deep cave cut into layered red sandstone.
        [
            "....SSSS.....",
            "...SSsssS....",
            "..SSsssssSS..",
            ".SSddddsssSS.",
            "SSSSSSSSSSSSS",
            "SsssssssssssS",
            "SsssdddsssssS",
            "Sssdk..kdsssS",
            "Ssdk....kdssS",
            "Sdk......kdsS",
            "Sdk......kdsS",
            "Sdk......kdsS",
            "SSSdddddddSSS"
        ],
        // Moab: a blue-bannered watchpost on a high, flat mesa.
        [
            ".......GBBB..",
            ".......GBB...",
            ".......G.....",
            ".....S.S.S...",
            ".....SSSSS...",
            ".....SsksS...",
            ".....SsksS...",
            "..SSSSSSSSS..",
            ".SSSSSSSSSSS.",
            "..SsssdddSS..",
            "...SssdddS...",
            "...SssdddS...",
            "..SSSSSSSSS.."
        ],
        // Jordan: white river ripples and a line of memorial / crossing stones.
        [
            ".BBBWWBBBBBB.",
            "BBbbbbbWWbbBB",
            "BbbSSSbbbbbBB",
            "BBBSSSBBWWBBB",
            "BWbbddbbbSSSB",
            "BbbWWbbbbSSSB",
            "BBBbbSSSbbddB",
            "BbbWWSSSbbBBB",
            "BSSSbbddbbWWB",
            "BSSSbbWWbbbbb",
            "BBddbbbSSSBBB",
            "BWWbbbbSSSbbB",
            ".BBBBBBBddBB."
        ],
        // Jericho: three fortified towers and the final bronze gate.
        [
            ".....G.G.....",
            ".....SSS.....",
            ".G.G.SkS.G.G.",
            ".SSS.SSS.SSS.",
            ".SkS.SkS.SkS.",
            ".SSSSSSSSSSS.",
            ".SsSssSssSsS.",
            ".SsSsGGGsSsS.",
            ".SsSGGgGGSsS.",
            ".SsSGgggGSsS.",
            ".SsSGgkgGSsS.",
            ".SsSGgggGSsS.",
            "SSSSSSSSSSSSS"
        ],
        // Dophkah: a bronze quarry pick above stepped blocks of cut stone.
        [
            "..GGGG.......",
            ".GG..dG......",
            ".....d.......",
            "....d........",
            "...d.........",
            "..d...SSSS...",
            ".....SSssS...",
            "..SSSSSSSS...",
            ".SSssSsssS...",
            ".SSssSsssSSS.",
            "SSSSSSSSSSsSS",
            "SssssSssssssS",
            "SSSSSSSSSSSSS"
        ],
        // Wilderness camps: an azure-striped travel pavilion under a crescent moon.
        [
            ".GG..........",
            "GG...........",
            "GG.G....G....",
            ".GG.....s....",
            ".......WBW...",
            "......WWBWW..",
            ".....WWBBBWW.",
            "....WWBBsBBWW",
            "....WBBsksBBW",
            "....WBskkksBW",
            "....WWBkkkBWW",
            ".gg.SSSSSSSSS",
            "gg....gg....."
        ],
        // Ezion-geber: a white-sailed harbor boat, wooden jetty and blue water.
        [
            "......G......",
            ".....WG......",
            "....WWG......",
            "...WWWG......",
            "..WWWWG......",
            ".WWWWWG......",
            "......G...ddd",
            "...GGGGGGG.d.",
            "....gdddgg.d.",
            "BBBBgggggBBdB",
            "BWWBBBBBBBBdB",
            "BBBBBWWBBBBBB",
            ".BBBBBBBWWBB."
        ],
        // Edom: two red sandstone cliffs enclosing a narrow gold pass.
        [
            "..FF.....FF..",
            ".FffF...FffF.",
            "FffffF.FffffF",
            "FffffF.FffffF",
            "FddffF.FffddF",
            "FdddfF.FfdddF",
            "FddddF.FddddF",
            "FFdddFsFdddFF",
            "FfdddFsFdddfF",
            "FffddGsGddffF",
            "FfdddGsGdddfF",
            ".FddGsssGddF.",
            "..FGsssssGF.."
        ],
        // Nebo: a high white peak with an overlook staff above green foothills.
        [
            "......G......",
            "......G......",
            "......G......",
            ".....WWW.....",
            "....WSSSW....",
            "...WSSdSSW...",
            "..WSSdddSSW..",
            ".SSSdddddSSS.",
            "SSSdddddddSSS",
            "SSdddtTtdddSS",
            ".TtTTTTTTtTt.",
            "..tTTTTTTTt..",
            "...ttttttt..."
        ],
        // Ai: a compact hill fortress with a pennant and a stepped green approach.
        [
            "......GBBB...",
            "......GBB....",
            "......G......",
            "..S.S.S.S.S..",
            "..SSSSSSSSS..",
            "..SsSsGsSsS..",
            "..SsSGGGSsS..",
            "..SSSGkGSSS..",
            ".TTSSGGGSSTT.",
            "TTtSSSSSSStTT",
            "TtTTTSSSTTTtT",
            ".tTTTSSSTTTt.",
            "..tttSSSttt.."
        ]
    ];
}
