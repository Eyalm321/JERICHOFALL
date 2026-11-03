using System.Numerics;

namespace Jerichofall;

public readonly record struct FlightProfile(
    float Speed, float Gravity, float Drag, int FlightFrames, float Spin,
    int Ricochets, float HorizontalRebound, int Width, int Height);

public readonly record struct FlightStep(
    Vector2 Velocity, float Direction, bool Falling, bool Returning, bool Detonate);

public readonly record struct ProjectileContact(
    Vector2 Velocity, bool Falling, bool Returning, bool Resting, bool Detonate, int PickupDelay);

/// <summary>
/// Independent flight rules for the project's seven authored weapons. Their tuning comes
/// from our BiblicalProjectile and RelicProjectile controllers in the earlier native mod.
/// Falling, owner immunity and pickup timing follow the separately documented combat
/// behavior. Geometry, target selection and damage stay with the arena simulation.
/// </summary>
public static class CombatPhysics
{
    public const int AmmoCapacity = 6;
    public const int WallPickupDelay = 15;
    public const int DroppedOwnerCatchDelay = 25;
    public const int CatchPauseFrames = 10;
    public const float OwnerProtectionDistanceSquared = 400;
    public const float EmberRadius = 23;
    public const float StormRadius = 38;
    public const float DiscReturnDistanceSquared = 100;

    public static FlightProfile Profile(Equipment kind) => kind switch
    {
        Equipment.Stone => new(5.6f, .11f, .997f, 75, 0, 0, .4f, 4, 4),
        Equipment.Staff => new(4.8f, .14f, .993f, 65, .32f, 1, .65f, 6, 4),
        Equipment.Jawbone => new(4.3f, .19f, .988f, 52, .23f, 2, .72f, 6, 4),
        Equipment.Spear => new(6.4f, .04f, .999f, 90, 0, 0, .15f, 8, 3),
        Equipment.EmberVessel => new(4.5f, .16f, .989f, 54, .11f, 0, 0, 6, 6),
        Equipment.BronzeDisc => new(5.6f, 0, 1, 130, .34f, 1, .8f, 6, 6),
        Equipment.StormJavelin => new(8, .012f, 1, 34, 0, 0, 0, 4, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Supplies are not projectiles.")
    };

    public static Vector2 LaunchVelocity(Equipment kind, float direction, float? speedOverride = null)
    {
        var velocity = new Vector2(MathF.Cos(direction), MathF.Sin(direction)) * (speedOverride ?? Profile(kind).Speed);
        if (MathF.Abs(velocity.Y) <= .01f) velocity.Y = 0;
        if (kind == Equipment.EmberVessel) velocity.Y -= .9f;
        return velocity;
    }

    /// <param name="age">Flight tick including this update; first update is one.</param>
    /// <param name="ownerDelta">Shortest wrapped displacement to a living owner, when available.</param>
    public static FlightStep AdvanceFlight(Equipment kind, Vector2 velocity, int age, float direction,
        bool falling = false, bool returning = false, Vector2? ownerDelta = null)
    {
        if (falling)
        {
            float gravity = MathF.Abs(velocity.Y) <= 1 ? .06f : .2f;
            velocity = new(velocity.X * .98f, MathF.Min(5.5f, velocity.Y + gravity));
            if (velocity.Y < 0)
                direction += MathF.Max(1, MathF.Abs(1 - velocity.Y)) * (8 * MathF.PI / 180) * (velocity.X < 0 ? -1 : 1);
            else if (velocity.Y > 0)
                direction += Math.Clamp(AngleDelta(direction, MathF.Atan2(velocity.Y, velocity.X)), -MathF.PI / 45, MathF.PI / 45);
            return new(velocity, direction, true, returning, false);
        }

        var profile = Profile(kind);
        bool detonate = false;
        if (kind == Equipment.BronzeDisc)
        {
            returning |= age >= 19;
            if (returning && ownerDelta is { } delta && delta.LengthSquared() > .01f)
                velocity = Vector2.Lerp(velocity, Vector2.Normalize(delta) * 5.8f, .16f);
            falling = age >= profile.FlightFrames;
        }
        else
        {
            float vertical = velocity.Y + profile.Gravity;
            velocity = new(velocity.X * profile.Drag, kind == Equipment.StormJavelin ? vertical : MathF.Min(5.5f, vertical));
            detonate = kind == Equipment.EmberVessel && age >= profile.FlightFrames;
            falling = kind == Equipment.StormJavelin ? age >= profile.FlightFrames : age > profile.FlightFrames;
        }
        direction = profile.Spin != 0 ? direction + profile.Spin :
            velocity.LengthSquared() > .01f ? MathF.Atan2(velocity.Y, velocity.X) : direction;
        return new(velocity, direction, falling, returning, detonate);
    }

    /// <summary>The raider's ordinary arrow has a straight first 25 ticks, unlike the heroes' ballistic weapons.</summary>
    public static FlightStep AdvanceArrow(Vector2 velocity, int age, float direction, bool falling = false)
    {
        if (falling) return AdvanceFlight(Equipment.Stone, velocity, age, direction, true);
        if (age > 25)
        {
            velocity = new(MathF.CopySign(MathF.Max(0, MathF.Abs(velocity.X) - .03f), velocity.X),
                MathF.Min(5.5f, velocity.Y + .2f));
            if (velocity.LengthSquared() > .01f) direction = MathF.Atan2(velocity.Y, velocity.X);
        }
        return new(velocity, direction, false, false, false);
    }

    /// <param name="bounceCount">Total terrain contacts including this contact.</param>
    public static ProjectileContact HitTerrain(Equipment kind, Vector2 velocity, bool horizontal,
        int bounceCount, bool falling = false, bool returning = false, float direction = 0)
    {
        if (kind == Equipment.EmberVessel) return new(Vector2.Zero, false, false, false, true, 0);
        var profile = Profile(kind);
        if (kind <= Equipment.Spear)
        {
            if (!horizontal && velocity.Y > 0 && velocity.Y < 1.15f)
                return new(Vector2.Zero, false, false, true, false, 0);
            velocity = horizontal ? new(-velocity.X * profile.HorizontalRebound, velocity.Y * .75f) :
                new(velocity.X * .72f, -velocity.Y * (profile.Ricochets > 0 ? .5f : .35f));
            return new(velocity, bounceCount > profile.Ricochets, false, false, false, 0);
        }
        if (kind == Equipment.BronzeDisc && bounceCount <= 1)
        {
            velocity = horizontal ? new(-velocity.X * .8f, velocity.Y) : new(velocity.X, -velocity.Y * .8f);
            return new(velocity, falling, true, false, false, 0);
        }

        bool dangerous = IsDangerous(velocity, direction, falling);
        if (falling && !dangerous)
        {
            if (horizontal) return new(new(-velocity.X, velocity.Y), true, returning, false, false, 0);
            if (velocity.Y < 0) return new(new(velocity.X, 0), true, returning, false, false, 0);
            return new(Vector2.Zero, false, returning, true, false, 0);
        }
        // A near-parallel shot skims the surface instead of sticking to it.
        if (horizontal && velocity.Y < 0 && MathF.Abs(velocity.X / velocity.Y) < .1f)
            return new(new(0, velocity.Y), falling, returning, false, false, 0);
        if (!horizontal && (velocity.Y < 0 || !falling) && velocity.X != 0 && MathF.Abs(velocity.Y / velocity.X) < .1f)
            return new(new(velocity.X, 0), falling, returning, false, false, 0);
        return new(Vector2.Zero, false, returning, true, false, WallPickupDelay);
    }

    public static bool IsDangerous(Vector2 velocity, float direction, bool falling) =>
        !falling || velocity.Y >= 5.5f && MathF.Abs(AngleDelta(direction, MathF.PI / 2)) <= MathF.PI / 45;

    public static bool Overlaps(Equipment kind, Vector2 projectile, Vector2 targetCenter, float width = 8, float height = 14)
    {
        var profile = Profile(kind);
        var delta = ArenaLayout.Delta(projectile, targetCenter);
        return MathF.Abs(delta.X) < (width + profile.Width) * .5f && MathF.Abs(delta.Y) < (height + profile.Height) * .5f;
    }

    public static bool HitsTerrain(ArenaLayout layout, Equipment kind, Vector2 center)
    {
        var p = Profile(kind);
        return layout.Blocked(center + new Vector2(0, p.Height * .5f), p.Width, p.Height);
    }

    public static bool HasLineOfSight(ArenaLayout layout, Vector2 origin, Vector2 target)
    {
        var delta = ArenaLayout.Delta(origin, target);
        int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length()));
        for (int i = 1; i <= steps; i++)
            if (layout.Point(ArenaLayout.Wrap(origin + delta * ((float)i / steps)))) return false;
        return true;
    }

    private static float AngleDelta(float from, float to) => MathF.IEEERemainder(to - from, MathF.Tau);
}
