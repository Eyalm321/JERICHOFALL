using System.Numerics;

namespace Jerichofall.Motion;

/// <summary>Both queries must use the host's wrap-aware collision geometry.</summary>
public interface ILedgeGeometry
{
    bool BodyIntersectsSolid(Vector2 position);
    bool SolidContainsPoint(Vector2 point);
}

public readonly record struct LedgeCandidate(int EdgeY, float BodyY, int Direction);

/// <summary>Geometry portion of ledge acquisition; state eligibility and X attachment are external.</summary>
public static class LedgeProbe
{
    public static bool TryFind(Vector2 position, int direction, ILedgeGeometry geometry,
        out LedgeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        for (int rise = 0; rise < 10; rise++)
        {
            int edgeY = (int)position.Y - rise;
            if (!CanGrabAt(position, edgeY, direction, geometry)) continue;
            candidate = new(edgeY, edgeY + 2, direction);
            return true;
        }
        candidate = default;
        return false;
    }

    public static bool CanGrabAt(Vector2 position, int edgeY, int direction, ILedgeGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        float sideX = position.X + 6 * direction;
        return !geometry.BodyIntersectsSolid(new(position.X, edgeY + 2)) &&
               !geometry.BodyIntersectsSolid(position + new Vector2(0, 5)) &&
               !geometry.SolidContainsPoint(new(sideX, edgeY - 1)) &&
               geometry.SolidContainsPoint(new(sideX, edgeY));
    }
}
