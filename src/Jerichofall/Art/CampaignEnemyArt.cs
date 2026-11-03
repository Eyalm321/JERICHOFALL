using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;


namespace Jerichofall.View;

/// <summary>Imports the authored animation atlas into crisp game-resolution sprites.</summary>
public static class CampaignEnemyArt
{
    private const int Width = 48, Height = 40, AnchorX = 24, AnchorY = 36;
    private static readonly List<Texture2D> owned = [];
    private static readonly Texture2D[,] frames = new Texture2D[6, 4];
    private static readonly Texture2D[,] masks = new Texture2D[6, 4];
    private static readonly int[] heights = [21, 15, 17, 21, 23, 29];
    private static readonly Vector2[] outlineOffsets = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
    private static readonly List<EnemyFrameInfo> diagnostics = [];
    public static IReadOnlyList<EnemyFrameInfo> Frames => diagnostics;
    public static bool Initialized => owned.Count == 48;
    public sealed record EnemyFrameInfo(string Kind, int Frame, int VisiblePixels, int Width, int Height, string PixelHash);

    public static void Initialize(string directory)
    {
        if (Initialized) return;
        using var stream = File.OpenRead(Path.Combine(directory, "enemies.png"));
        using var source = Texture2D.FromStream(Canvas2D.Device, stream);
        if (source.Width % 4 != 0 || source.Height % 6 != 0 || source.Width / 4 != source.Height / 6)
            throw new InvalidDataException("Enemy atlas must contain four columns and six rows of square cells.");
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        var cell = source.Width / 4;
        for (var row = 0; row < 6; row++)
        {
            var boxes = Enumerable.Range(0, 4).Select(column => Bounds(pixels, source.Width, cell, column, row)).ToArray();
            var scale = heights[row] / (float)boxes.Max(box => box.Height);
            for (var column = 0; column < 4; column++)
            {
                var box = boxes[column];
                var feet = Feet(pixels, source.Width, box);
                var target = new Color[Width * Height];
                // Sample on the engine's native pixel grid. All four poses share one scale and a foot anchor.
                for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                {
                    var sx = (int)Math.Floor(feet + (x - AnchorX + .5f) / scale);
                    var sy = (int)Math.Floor(box.Bottom + (y - AnchorY + .5f) / scale);
                    if (sx < column * cell || sx >= (column + 1) * cell || sy < row * cell || sy >= (row + 1) * cell) continue;
                    var color = pixels[sy * source.Width + sx];
                    if (color.A < 200) continue;
                    color.A = 255;
                    target[y * Width + x] = color;
                }
                var visible = target.Count(color => color.A > 0);
                if (visible < 35) throw new InvalidDataException($"Empty or unreadable enemy sprite: row {row}, frame {column}.");
                frames[row, column] = Create(target);
                masks[row, column] = Create(target.Select(color => color.A == 0 ? Color.Transparent : Color.White).ToArray());
                var raw = target.SelectMany(color => new[] { color.R, color.G, color.B, color.A }).ToArray();
                diagnostics.Add(new EnemyFrameInfo(((EnemyKind)row).ToString(), column, visible,
                    (int)Math.Ceiling(box.Width * scale), (int)Math.Ceiling(box.Height * scale),
                    Convert.ToHexStringLower(SHA256.HashData(raw))));
            }
        }
    }
    private static Rectangle Bounds(Color[] pixels, int width, int cell, int column, int row)
    {
        var left = (column + 1) * cell; var right = -1;
        var top = (row + 1) * cell; var bottom = -1;
        for (var y = row * cell; y < (row + 1) * cell; y++) for (var x = column * cell; x < (column + 1) * cell; x++)
        {
            if (pixels[y * width + x].A < 200) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left) throw new InvalidDataException("An animation cell in the enemy atlas is empty.");
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
    private static float Feet(Color[] pixels, int width, Rectangle box)
    {
        var left = box.Right; var right = box.Left;
        for (var y = box.Bottom - Math.Max(2, box.Height / 15); y < box.Bottom; y++)
            for (var x = box.Left; x < box.Right; x++)
                if (pixels[y * width + x].A >= 200) { left = Math.Min(left, x); right = Math.Max(right, x); }
        return (left + right + 1) * .5f;
    }
    private static Texture2D Create(Color[] pixels)
    {
        var texture = new Texture2D(Canvas2D.Device, Width, Height);
        texture.SetData(pixels); owned.Add(texture); return texture;
    }
    public static void Draw(EnemyKind kind, Vector2 feet, int facing, int frame, bool flash, float tell)
    {
        if (!Initialized) throw new InvalidOperationException("Campaign enemy art has not initialized.");
        var row = (int)kind; frame = Math.Clamp(frame, 0, 3);
        var effect = facing < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        var origin = new Vector2(AnchorX, AnchorY);
        feet = new Vector2((int)feet.X, (int)feet.Y);
        var mask = masks[row, frame];
        var ink = new Color(26, 16, 35);
        foreach (var offset in outlineOffsets)
            Canvas2D.SpriteBatch.Draw(mask, feet + offset, null, ink, 0, origin, 1, effect, 0);
        var texture = flash ? mask : frames[row, frame];
        var tint = flash ? Color.White : Color.Lerp(Color.White, new Color(255, 220, 160), Math.Clamp(tell, 0, 1) * .3f);
        Canvas2D.SpriteBatch.Draw(texture, feet, null, tint, 0, origin, 1, effect, 0);
    }
    public static void Dispose()
    {
        foreach (var texture in owned) texture.Dispose();
        owned.Clear(); diagnostics.Clear();
    }
}
