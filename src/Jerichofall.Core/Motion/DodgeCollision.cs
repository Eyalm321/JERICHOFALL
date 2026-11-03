using System.Numerics;

namespace Jerichofall.Motion;

/// <summary>Offsets are relative to the current body position at impact.</summary>
public interface IDodgeCollisionGeometry
{
    bool SolidAtOffset(Vector2 offset, bool smallBody);
    bool EnteringOneWayAtOffset(Vector2 offset, bool smallBody);
}

/// <summary>
/// Apply DirectPositionChange without another collision test, then integrate Nudge through
/// the normal pixel mover. Call these responses with the velocity before dodge drag.
/// </summary>
public readonly record struct DodgeCollisionResult(
    MovementState State, Vector2 DirectPositionChange, Vector2 Nudge);

public static class DodgeCollision
{
    public static DodgeCollisionResult Horizontal(MovementState state,
        IDodgeCollisionGeometry geometry, bool grounded, bool hasPlatform, float frameDelta = 1)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        int direction = Math.Sign(state.Velocity.X);
        if (grounded && !state.DodgeSlide)
        {
            if (!geometry.SolidAtOffset(new(direction, 0), true))
            {
                state.SmallHitbox = true;
                state.DodgeSlide = true;
                state.Velocity *= 1.4f;
                return new(state, new(direction, 0), Vector2.Zero);
            }
            state.SmallHitbox = false;
        }
        if (!geometry.SolidAtOffset(new(direction, -5), state.SmallHitbox))
            return new(state, Vector2.Zero, hasPlatform ? Vector2.Zero : new(0, -2 * frameDelta));
        if (!geometry.SolidAtOffset(new(direction, 5), state.SmallHitbox))
            return new(state, Vector2.Zero, hasPlatform ? Vector2.Zero : new(0, 2 * frameDelta));
        if (Math.Abs(state.Velocity.Y) > .02f)
            state.Velocity = new(0, state.Velocity.Length() * Math.Sign(state.Velocity.Y));
        return new(state, Vector2.Zero, Vector2.Zero);
    }

    public static DodgeCollisionResult Vertical(MovementState state,
        IDodgeCollisionGeometry geometry, bool hasPlatform, float frameDelta = 1)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (Math.Abs(state.Velocity.X) >= .02f)
        {
            state.Velocity = new(state.Velocity.Length() * Math.Sign(state.Velocity.X), 0);
            return new(state, Vector2.Zero, Vector2.Zero);
        }
        if (hasPlatform) return new(state, Vector2.Zero, Vector2.Zero);
        int vertical = state.Velocity.Y > 0 ? 1 : -1;
        foreach (int side in new[] { -1, 1 })
        {
            var far = new Vector2(side * 5, vertical);
            var near = new Vector2(side * 3, vertical);
            bool solidOpen = !geometry.SolidAtOffset(far, state.SmallHitbox) ||
                             !geometry.SolidAtOffset(near, state.SmallHitbox);
            bool oneWayOpen = vertical < 0 ||
                              !geometry.EnteringOneWayAtOffset(far, state.SmallHitbox) ||
                              !geometry.EnteringOneWayAtOffset(near, state.SmallHitbox);
            if (solidOpen && oneWayOpen)
                return new(state, Vector2.Zero, new(side * 2 * frameDelta, 0));
        }
        return new(state, Vector2.Zero, Vector2.Zero);
    }
}
