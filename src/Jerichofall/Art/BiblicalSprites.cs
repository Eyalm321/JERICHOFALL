using Microsoft.Xna.Framework;

namespace Jerichofall.View;

/// <summary>Hand-authored native-pixel costumes, heads and weapons with shared animation poses.</summary>
internal static class BiblicalSprites
{
    private static readonly Color Outline = new(15, 20, 37);
    private static readonly Color Linen = new(225, 216, 179);
    private static readonly Color LinenShade = new(155, 148, 127);
    private static readonly Color Skin = new(217, 157, 108);
    private static readonly Color SkinShade = new(153, 91, 65);
    private static readonly Color Hair = new(71, 45, 39);
    private static readonly Color HairLight = new(116, 69, 41);
    private static readonly Color Leather = new(111, 69, 42);
    private static readonly Color Gold = new(230, 191, 99);

    public static Color[] Render(string role, string id, int width, int height, int frameWidth, int frameHeight, Color garment, Color highlight)
    {
        var result = new Color[width * height];
        var samson = id == "samson";
        var moses = id == "moses";
        var joshua = id == "joshua";
        var hair = moses ? new Color(155, 159, 163) : samson || joshua ? Hair : new Color(113, 53, 28);
        var hairLight = moses ? new Color(237, 235, 216) : samson || joshua ? HairLight : new Color(177, 90, 36);
        for (var fy = 0; fy + frameHeight <= height; fy += frameHeight)
            for (var fx = 0; fx + frameWidth <= width; fx += frameWidth)
            {
                var frame = fy / frameHeight * (width / frameWidth) + fx / frameWidth;
                void P(int x, int y, Color c) { if (x >= 0 && x < frameWidth && y >= 0 && y < frameHeight) result[(fy + y) * width + fx + x] = c; }
                void R(int x, int y, int w, int h, Color c) { for (var py = y; py < y + h; py++) for (var px = x; px < x + w; px++) P(px, py, c); }
                if (role == "body")
                {
                    var slide = frame is 10 or 11;
                    var duck = frame == 9;
                    var top = duck ? 11 : 7;
                    if (slide)
                    {
                        // Two own compact tumble poses, with the separately attached head visible.
                        R(2, 12, 8, 6, Outline); R(3, 12, 5, 4, garment); R(5, 13, 4, 3, Linen);
                        R(2, 16, 5, 2, Leather);
                        if (joshua) R(5, 13, 4, 2, Gold);
                        if (moses) R(7, 15, 3, 2, Linen);
                        P(3 + frame % 4, 12, highlight); continue;
                    }
                    var left = samson ? 2 : 3;
                    var torsoWidth = samson ? 8 : 6;
                    R(left, top, torsoWidth, 10, Outline);
                    R(left + 1, top + 1, torsoWidth - 2, 8, Linen);
                    R(left + 1, top + 4, torsoWidth - 2, 2, Leather);
                    R(left + 1, top + 1, samson ? 2 : 3, 4, garment);
                    P(left + 1, top + 1, highlight);
                    R(left + 1, top + 7, torsoWidth - 2, 2, LinenShade);
                    for (var x = left + 1; x < left + torsoWidth - 1; x++) P(x, top + 8, x % 2 == 0 ? Gold : garment);
                    R(left - 1, top + 2, 2, samson ? 5 : 4, SkinShade);
                    R(left + torsoWidth - 1, top + 2, 2, samson ? 5 : 4, Skin);
                    if (samson) { R(left - 1, top + 5, 2, 2, Leather); R(left + torsoWidth - 1, top + 5, 2, 2, Leather); }
                    else { P(left + torsoWidth, top + 4, Leather); P(left + torsoWidth, top + 5, Gold); }
                    var run = frame is 1 or 2 or 3;
                    var legA = run && frame % 2 == 0 ? -1 : 0;
                    var legB = run && frame % 2 != 0 ? 1 : 0;
                    R(left + 1 + legA, 17, 2, 3, Leather); R(left + torsoWidth - 3 + legB, 17, 2, 3, Leather);
                    P(left + 2 + legA, 17, Skin); P(left + torsoWidth - 2 + legB, 17, Skin);
                    if (moses)
                    {
                        // Long linen robe, wide sleeves and a diagonal blue mantle.
                        R(2, top + 2, 2, 5, Linen); R(8, top + 2, 2, 5, LinenShade);
                        R(3, 14, 6, 5, Linen); R(3, 18, 6, 1, LinenShade);
                        R(4, 12, 4, 2, garment); R(6, 13, 2, 5, garment); P(7, 13, Gold);
                    }
                    if (joshua)
                    {
                        // Bronze scales and a trailing blue cape distinguish the soldier at game scale.
                        R(1, top + 1, 2, 9, garment); P(1, top + 1, highlight);
                        R(4, top + 1, 4, 5, Leather);
                        for (var y = top + 1; y < top + 6; y++)
                            for (var x = 4; x < 8; x++) if ((x + y) % 2 == 0) P(x, y, Gold);
                        R(4, top + 6, 4, 2, garment); P(7, top + 6, Gold);
                    }
                    if (frame == 4) // Knees tuck on ascent; the pose does not change the collision box.
                    {
                        R(3, 18, 6, 2, Color.Transparent); R(2, 16, 3, 2, Leather); R(7, 15, 3, 2, Leather);
                        P(3, 16, Skin); P(8, 15, Skin);
                    }
                    if (frame is 6 or 8) // Falling and air dodge expose the mantle and spread hands.
                    {
                        R(0, top + 2, 3, 2, SkinShade); R(9, top + 1, 3, 2, Skin);
                        P(0, top + 2, Gold); P(11, top + 1, Gold);
                    }
                    if (frame == 7) // Raised grip on a wall or ledge.
                    {
                        R(1, 4, 2, 7, SkinShade); R(9, 3, 2, 8, Skin); R(1, 3, 3, 2, Leather); R(9, 2, 3, 2, Leather);
                    }
                }
                else if (role.StartsWith("head", StringComparison.Ordinal))
                {
                    var backwards = frame == 3;
                    var duck = frame == 4;
                    var offsetY = duck ? 1 : 0;
                    R(3, 1 + offsetY, 6, 6, Outline);
                    R(4, 2 + offsetY, 4, 5, SkinShade); R(5, 2 + offsetY, 4, 4, Skin);
                    R(3, offsetY, 5, 3, hair); P(4, offsetY, hairLight); P(6, 1 + offsetY, hairLight);
                    if (samson)
                    {
                        R(2, 2 + offsetY, 2, 7 - offsetY, Hair); R(3, 6 + offsetY, 2, 3 - offsetY, HairLight);
                        R(3, 2 + offsetY, 5, 1, garment); P(6, 2 + offsetY, highlight);
                    }
                    else { R(3, 2 + offsetY, 4, 1, garment); P(7, 2 + offsetY, Gold); }
                    if (moses)
                    {
                        R(2, 2 + offsetY, 2, 5, hair); R(3, 1 + offsetY, 5, 2, hairLight);
                        R(4, 6 + offsetY, 5, 2, hairLight); R(5, 8 + offsetY, 3, 1, hairLight);
                        P(6, 9, hair); P(4, 7 + offsetY, hair);
                    }
                    if (joshua)
                    {
                        R(5, 6 + offsetY, 4, 1, Hair); P(5, 5 + offsetY, Hair);
                        if (role != "headNoHat")
                        {
                            R(2, 1 + offsetY, 6, 3, Leather); R(3, 1 + offsetY, 6, 2, Gold);
                            R(3, offsetY, 3, 1, garment); P(4, offsetY, highlight); R(3, 4 + offsetY, 1, 3, Gold);
                        }
                    }
                    var eyeX = backwards ? 4 : 8;
                    var eyeY = (frame == 1 ? 3 : frame == 2 ? 5 : 4) + offsetY;
                    P(eyeX, eyeY, Outline); P(backwards ? eyeX + 1 : eyeX - 1, eyeY, Color.White);
                    if (role == "headCrown") { R(3, 0, 6, 2, Gold); P(3, 0, Linen); P(5, 0, Linen); P(8, 0, Linen); }
                }
                else if (role == "bow")
                {
                    if (moses)
                    {
                        R(0, 3, Math.Min(frameWidth, 12), 2, Leather);
                        R(1, 3, Math.Min(frameWidth - 1, 10), 1, HairLight);
                        R(Math.Min(frameWidth - 2, 10), 1, 2, 3, Leather);
                        P(Math.Min(frameWidth - 3, 9), 1, Gold);
                    }
                    else if (joshua)
                    {
                        R(0, 3, frameWidth - 3, 1, Leather);
                        R(frameWidth - 4, 2, 3, 3, Gold); P(frameWidth - 1, 3, Linen);
                        P(frameWidth - 3, 1, Gold); P(frameWidth - 3, 5, Leather);
                    }
                    else if (samson)
                    {
                        R(2, 3, 7, 2, Outline); R(3, 3, 6, 1, Linen); P(8, 2, Linen); P(9, 3, Linen); P(8, 5, LinenShade);
                        P(3, 5, Linen); P(5, 5, Linen); P(7, 5, Linen);
                    }
                    else
                    {
                        // Two leather cords converge on a stone pouch; the drawn pose extends the sling.
                        var stretched = frame < 2;
                        P(2, 3, Leather); P(3, 2, Gold); P(3, 4, Leather); P(4, stretched ? 1 : 3, Leather);
                        P(4, stretched ? 5 : 4, Leather); P(5, stretched ? 0 : 4, Leather); P(5, stretched ? 6 : 5, Leather);
                        P(6, stretched ? 0 : 5, SkinShade); P(6, stretched ? 6 : 6, SkinShade);
                        if (frame != 4) { P(6, stretched ? 3 : 5, LinenShade); P(7, stretched ? 3 : 5, Linen); }
                    }
                }
                else if (role == "corpse")
                {
                    var headX = frame == 0 ? 8 : frame == 3 ? 2 : frame == 4 ? 1 : 4;
                    var headY = frame == 0 ? 10 : frame == 3 ? 5 : frame == 4 ? 3 : 1;
                    if (frame == 0)
                    {
                        R(1, 13, 10, 5, Outline); R(2, 13, 8, 4, Linen); R(2, 13, 3, 4, garment); R(0, 16, 5, 2, Leather);
                    }
                    else if (frame == 3)
                    {
                        R(3, 9, 6, 8, Outline); R(4, 10, 4, 6, Linen); R(4, 10, 2, 4, garment);
                        R(6, 16, 5, 3, Leather); R(2, 10, 2, 5, SkinShade);
                    }
                    else if (frame == 4)
                    {
                        R(3, 7, 6, 8, Outline); R(4, 7, 4, 7, Linen); R(3, 8, 3, 3, garment);
                        R(7, 13, 4, 3, Leather); R(9, 15, 2, 3, Leather); R(2, 8, 2, 4, SkinShade);
                    }
                    else
                    {
                        R(3, 6, 7, 10, Outline); R(4, 7, 5, 8, Linen); R(4, 7, 2, 5, garment);
                        R(4, 15, 2, 4, Leather); R(8, 15, 2, 4, Leather);
                        if (frame == 5) { R(1, 3, 2, 8, SkinShade); R(1, 3, 3, 2, Leather); }
                        else { R(1, 8, 3, 2, SkinShade); R(9, 7, 3, 2, Skin); }
                    }
                    R(headX, headY, 4, 5, Outline); R(headX, headY, 4, 2, hair);
                    R(headX + 1, headY + 2, 3, 2, Skin); P(headX + 2, headY + 2, SkinShade);
                    if (samson) R(headX, headY + 1, 1, 6, hair);
                    if (moses) R(headX + 1, headY + 4, 3, 2, hairLight);
                    if (joshua) { R(headX, headY, 4, 2, Gold); P(5, frame == 0 ? 14 : 10, Gold); }
                }
            }
        return result;
    }
}
