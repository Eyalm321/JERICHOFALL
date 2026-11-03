using System.Numerics;

namespace Jerichofall.Motion;

/// <summary>The host queries the complete body shape at each proposed integer position.</summary>
public interface IMovementCollision
{
    bool SolidAt(Vector2 position);
    bool EnteringOneWay(Vector2 previousPosition, Vector2 proposedPosition);
}

public readonly record struct PixelBody(Vector2 Position, Vector2 Remainder);
public readonly record struct PixelMoveResult(PixelBody Body, bool HitHorizontal, bool HitVertical);

/// <summary>
/// Fractional accumulation with midpoint-to-even rounding, then horizontal-first pixel
/// stepping. One-way surfaces block only descending movement. Queries may implement wrap.
/// </summary>
public static class PixelMovement
{
    public static PixelMoveResult Integrate(PixelBody body, Vector2 displacement,
        IMovementCollision collision, bool ignoreOneWay = false)
    {
        ArgumentNullException.ThrowIfNull(collision);
        if (!float.IsFinite(displacement.X) || !float.IsFinite(displacement.Y))
            throw new ArgumentOutOfRangeException(nameof(displacement));
        var position = body.Position;
        var remainder = body.Remainder + displacement;
        bool hitH = MoveAxis(ref position, ref remainder, true, collision, ignoreOneWay);
        bool hitV = MoveAxis(ref position, ref remainder, false, collision, ignoreOneWay);
        return new(new(position, remainder), hitH, hitV);
    }

    private static bool MoveAxis(ref Vector2 position, ref Vector2 remainder, bool horizontal,
        IMovementCollision collision, bool ignoreOneWay)
    {
        float subpixel = horizontal ? remainder.X : remainder.Y;
        int amount = checked((int)Math.Round(subpixel, MidpointRounding.ToEven));
        if (horizontal) remainder.X -= amount;
        else remainder.Y -= amount;
        Vector2 step = horizontal ? new(Math.Sign(amount), 0) : new(0, Math.Sign(amount));
        for (int left = Math.Abs(amount); left > 0; left--)
        {
            var candidate = position + step;
            if (collision.SolidAt(candidate) ||
                (!horizontal && amount > 0 && !ignoreOneWay && collision.EnteringOneWay(position, candidate)))
            {
                if (horizontal) remainder.X = 0;
                else remainder.Y = 0;
                return true;
            }
            position = candidate;
        }
        return false;
    }

    /// <summary>Matches the shipped one-period correction, including its far-negative limit.</summary>
    public static float WrapCoordinate(float value, float period) =>
        (value < 0 ? value + period : value) % period;
}
