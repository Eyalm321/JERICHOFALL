using System.Numerics;

namespace Jerichofall.Motion;

/// <summary>
/// Deterministic movement rules for the game simulation. Step changes velocity
/// and requests displacement; a host supplies collision facts and applies the displacement.
/// Rendering, sound, player damage, inventory, and collision geometry are separate systems.
/// </summary>
public static class MovementKernel
{
    public static MovementResult Step(MovementState state, MovementInput input,
        MovementEnvironment world, float frameDelta = 1,
        Func<MovementState, Vector2, MovementState>? resolveMovement = null)
    {
        if (!float.IsFinite(frameDelta) || frameDelta <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameDelta));
        if (input.MoveX is < -1 or > 1 || input.MoveY is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(input), "Movement axes must be -1, 0, or 1.");

        var next = state;
        next.Facing = next.Facing < 0 ? -1 : 1;
        next.SlipperyControl = world.Grounded
            ? (world.SlipperyGround ? 0 : 1)
            : Math.Min(1, next.SlipperyControl + .1f);
        if (world.DoubleJumpVariant && (world.Grounded || next.Mode == MovementMode.Ledge))
            next.DoubleJumpAvailable = true;
        // Player input captures forced movement before the scheduler clears it.
        int drive = next.ForcedDirection != 0 ? next.ForcedDirection : input.MoveX;
        if (next.Mode != MovementMode.Dodge)
            next.DodgeCooldown = Math.Max(0, next.DodgeCooldown - frameDelta);
        next.ForcedFrames = Math.Max(0, next.ForcedFrames - frameDelta);
        if (next.ForcedFrames == 0) next.ForcedDirection = 0;
        if (next.Mode == MovementMode.Frozen)
        {
            if (input.MoveX != 0) next.Facing = input.MoveX;
            return new(next, Vector2.Zero, MovementEvent.None);
        }

        next.JumpBuffer = input.JumpPressed ? 6 : next.JumpBuffer - frameDelta;
        if (world.Grounded && next.Velocity.Y > -.02f)
        {
            next.JumpGrace = Math.Max(6, next.JumpGrace);
            next.WallFallCap = .5f;
            next.FlapGravity = 1;
            next.LedgeAwayDirection = 0;
        }
        else next.JumpGrace -= frameDelta;
        if (next.RoofBounceLock > 0)
            next.RoofBounceLock = next.Velocity.Y <= 0 ? 0 : next.RoofBounceLock - frameDelta;
        next.Gliding = false;

        bool wasDodging = next.Mode == MovementMode.Dodge;
        var result = next.Mode switch
        {
            MovementMode.Dodge => AdvanceDodge(next, input, world, drive, frameDelta, resolveMovement),
            MovementMode.Duck => AdvanceDuck(next, input, world, drive, frameDelta),
            MovementMode.Ledge => AdvanceLedge(next, input, world, drive),
            _ => AdvanceNormal(next, input, world, drive, frameDelta)
        };
        // Dodge impacts must change velocity before drag and jump/exit decisions.
        // Other modes resolve their requested motion after selecting the impulse.
        return !wasDodging && resolveMovement is not null
            ? result with { State = resolveMovement(result.State, result.Displacement) }
            : result;
    }

    public static float RunLimit(MovementEnvironment world)
    {
        float limit = 1.5f - (world.EncumbranceVariant ? world.ArrowCount / 2 * .25f : 0);
        if (world.SpeedBoots) limit *= 1.4f;
        if (world.Grounded && world.InMud) limit *= .4f;
        return limit;
    }

    private static MovementResult AdvanceNormal(MovementState s, MovementInput i,
        MovementEnvironment w, int drive, float dt)
    {
        bool duck = w.Grounded && drive == 0 && i.MoveY == 1;
        if (!w.HotCoals && duck && !s.StartAimingDown)
        {
            s.Mode = MovementMode.Duck;
            s.SmallHitbox = true;
            return new(s, Vector2.Zero, MovementEvent.DuckStarted);
        }
        if (!duck) s.StartAimingDown = false;

        var velocity = s.Velocity;
        float traction = .35f + .65f * s.SlipperyControl;
        float maximum = RunLimit(w);
        if (w.InMud && w.Grounded && drive != Math.Sign(velocity.X))
            velocity.X = Toward(velocity.X, 0, .1f); // This branch does not multiply TimeMult.
        else if ((i.Aiming && w.Grounded) ||
                 (!i.Aiming && s.SlipperyControl == 1 && drive != Math.Sign(velocity.X)))
        {
            float friction = w.Wings ? (Math.Abs(velocity.X) > maximum ? .14f : .2f)
                : (w.Grounded ? .2f : .14f);
            velocity.X = Toward(velocity.X, 0, friction * traction * dt);
        }
        if (!i.Aiming && drive != 0)
        {
            float acceleration = Math.Abs(velocity.X) > maximum && Math.Sign(velocity.X) == drive
                ? .03f
                : (w.Grounded ? .15f : .1f) * traction *
                  (s.DodgeCooldown > 0 ? .8f : 1) * (w.SpeedBoots ? 1.4f : 1);
            velocity.X = Toward(velocity.X, maximum * drive, acceleration * dt);
        }

        s.ClingDirection = 0;
        MovementEvent events = MovementEvent.None;
        if (!w.Grounded)
        {
            s.FlapGravity = Toward(s.FlapGravity, 1, (s.FlapGravity < .5f ? .012f : .048f) * dt);
            if (s.AutoBounce && velocity.Y > 0) s.AutoBounce = false;
            float gravity = velocity.Y <= 1 && (i.JumpHeld || s.AutoBounce) && s.VariableJump
                ? .15f : .3f;
            float fallLimit = 2.8f;
            if (drive != 0 && !i.Aiming && i.MoveY != 1 && CanWall(w, drive))
            {
                fallLimit = s.WallFallCap;
                s.WallFallCap = Toward(s.WallFallCap, 1.6f, .01f * dt);
                s.ClingDirection = drive;
                events |= MovementEvent.WallSlide;
            }
            else if (i.MoveY == 1 && velocity.Y > 0)
            {
                fallLimit = 3.5f;
                events |= MovementEvent.FastFall;
            }
            else if (i.JumpHeld && w.Wings && velocity.Y >= -1)
            {
                fallLimit = .8f;
                s.Gliding = true;
                events |= MovementEvent.Glide;
            }
            velocity.Y = Toward(velocity.Y, fallLimit, gravity * s.FlapGravity * dt);
        }
        s.Velocity = velocity;
        if (CanStartDodge(s, i, w))
            return StartDodge(s, i, w, drive, events);

        if (w.HotCoals) events |= ApplyCoals(ref s, i);
        else if (i.JumpPressed || s.JumpBuffer > 0)
        {
            if (s.JumpGrace > 0)
            {
                int ledge = i.MoveX == s.LedgeAwayDirection ? s.LedgeAwayDirection : 0;
                ApplyJump(ref s, i, w.JumpPad, false, ledge, drive);
                events |= MovementEvent.Jump;
            }
            else if (CanWall(w, -1) || CanWall(w, 1))
            {
                ApplyWallJump(ref s, CanWall(w, -1) ? 1 : -1);
                events |= MovementEvent.WallJump;
            }
            else if (s.DoubleJumpAvailable)
            {
                s.DoubleJumpAvailable = false;
                ApplyJump(ref s, i, false, true, 0, drive);
                events |= MovementEvent.DoubleJump;
            }
            else if (w.Wings && s.RoofBounceLock <= 0)
            {
                ClearJumpRequests(ref s);
                s.Velocity = s.Velocity with { Y = -1.4f };
                s.VariableJump = true;
                s.FlapGravity = 0;
                events |= MovementEvent.WingFlap;
            }
        }
        if (drive != 0) s.Facing = drive;
        var displacement = s.Velocity * dt;
        if (s.Velocity.Y is > -3.2f and < -1 && i.JumpHeld && w.InsideJumpThrough)
            displacement.Y -= 2 * dt;
        return new(s, displacement, events);
    }

    private static bool CanWall(MovementEnvironment w, int side) =>
        !w.Wings && !w.SolidFiveBelow && (side < 0 ? w.WallLeft : w.WallRight);

    private static bool CanStartDodge(MovementState s, MovementInput i, MovementEnvironment w) =>
        s.DodgeCooldown <= 0 && i.DodgePressed && !w.NoDodgeVariant;

    private static MovementResult StartDodge(MovementState s, MovementInput i,
        MovementEnvironment w, int drive, MovementEvent events)
    {
        bool fromDuck = s.Mode == MovementMode.Duck;
        if (drive != 0 && s.Mode == MovementMode.Normal) s.Facing = drive;
        s.DodgeSlide = fromDuck || (w.Grounded && i.Aim.Y > 0 && !w.InMud);
        if (s.DodgeSlide) s.SmallHitbox = true;
        Vector2 direction = i.Aim == Vector2.Zero || fromDuck
            ? new(s.Facing, 0) : EightWay(i.Aim);
        float speed = 5.5f * (w.SuperDodgeVariant ? 1.8f : s.DodgeSlide ? 1.4f : 1);
        if (w.InMud) speed *= .4f;
        s.Velocity = direction * speed;
        s.Mode = MovementMode.Dodge;
        s.DodgeFrames = 20;
        s.DodgeHoldFrames = 5;
        s.VariableJump = false;
        if (w.Grounded) s.JumpGrace = 12;
        return new(s, Vector2.Zero, events | MovementEvent.DodgeStarted);
    }

    private static MovementResult AdvanceDodge(MovementState s, MovementInput i,
        MovementEnvironment w, int drive, float dt,
        Func<MovementState, Vector2, MovementState>? resolveMovement)
    {
        if (w.HotCoals && (!s.DodgeSlide || w.CanStand))
        {
            var ev = ApplyCoals(ref s, i);
            FinishDodge(ref s, MovementMode.Normal, w.CanStand);
            return new(s, Vector2.Zero, ev | MovementEvent.DodgeEnded);
        }
        Vector2 displacement = Vector2.Zero;
        if (s.Velocity.Y < -.5f && w.InsideJumpThrough) displacement.Y -= 2 * dt;
        else if (s.Velocity.Y == 0 && w.InsideJumpThrough && !w.JumpThroughFiveAbove)
            displacement.Y -= dt;
        if (s.CatchPauseFrames > 0) s.CatchPauseFrames -= dt;
        else displacement += s.Velocity * dt;
        if (resolveMovement is not null) s = resolveMovement(s, displacement);
        s.Velocity *= (float)Math.Pow(.8500000238418579, dt);

        MovementEvent events = MovementEvent.None;
        if ((i.JumpPressed || s.JumpBuffer > 0) && (!s.DodgeSlide || w.CanStand))
        {
            int away = w.WallLeft ? 1 : w.WallRight ? -1 : 0;
            if (away != 0)
            {
                if ((int)s.DodgeFrames < 12)
                {
                    ApplyWallJump(ref s, away);
                    FinishDodge(ref s, MovementMode.Normal, w.CanStand);
                    return new(s, displacement, MovementEvent.WallJump | MovementEvent.DodgeEnded);
                }
                ClearJumpRequests(ref s);
                s.Velocity = new Vector2(away, -1) * (5.5f / MathF.Sqrt(2));
                s.DodgeFrames = 20;
                s.WallFallCap = .5f;
                s.FlapGravity = 1;
                s.Facing = away;
                s.VariableJump = true;
                events |= MovementEvent.WallJump;
            }
            else if (s.JumpGrace > 0)
            {
                if (s.DodgeSlide)
                {
                    s.SmallHitbox = false;
                    s.DodgeSlide = false;
                    s.ForcedDirection = Math.Sign(s.Velocity.X);
                    s.ForcedFrames = 16;
                    if (Math.Abs(s.Velocity.X) >= 1.75f)
                        s.Velocity = s.Velocity with { X = Math.Sign(s.Velocity.X) * 2.8f };
                }
                else s.Velocity = s.Velocity with { X = Math.Clamp(s.Velocity.X, -1.8f, 1.8f) };
                ApplyJump(ref s, i, w.JumpPad, false, 0, drive);
                FinishDodge(ref s, MovementMode.Normal, w.CanStand);
                return new(s, displacement, MovementEvent.Jump | MovementEvent.DodgeEnded);
            }
        }
        if (s.DodgeSlide && s.JumpGrace <= 0 && w.CanStand)
        {
            s.SmallHitbox = false;
            s.DodgeSlide = false;
        }
        if (!i.DodgePressed)
        {
            if (s.DodgeFrames > 0)
            {
                s.DodgeFrames -= dt;
                return new(s, displacement, events);
            }
            if (s.DodgeHoldFrames > 0 && i.DodgeHeld)
            {
                s.DodgeHoldFrames -= dt;
                return new(s, displacement, events);
            }
        }
        MovementMode exit = s.DodgeSlide &&
            ((w.Grounded && drive == 0 && i.MoveY == 1) || !w.CanStand)
            ? MovementMode.Duck : MovementMode.Normal;
        FinishDodge(ref s, exit, w.CanStand);
        return new(s, displacement, events | MovementEvent.DodgeEnded);
    }

    private static void FinishDodge(ref MovementState s, MovementMode mode, bool canStand)
    {
        if (s.DodgeSlide && canStand) s.SmallHitbox = false;
        s.DodgeSlide = false;
        s.CatchPauseFrames = 0;
        s.DodgeFrames = 0;
        s.DodgeCooldown = 25;
        s.Mode = mode;
        if (mode == MovementMode.Duck) s.SmallHitbox = true;
    }

    private static MovementResult AdvanceDuck(MovementState s, MovementInput i,
        MovementEnvironment w, int drive, float dt)
    {
        if (w.CanStand && CanStartDodge(s, i, w))
            return StartDodge(s, i, w, drive, MovementEvent.DuckEnded);
        if (i.JumpPressed && w.JumpThroughBelow && w.ClearThreeBelow)
        {
            ClearJumpRequests(ref s);
            s.Mode = MovementMode.Normal;
            s.SmallHitbox = false;
            return new(s, new Vector2(0, 3), MovementEvent.DropThrough | MovementEvent.DuckEnded);
        }
        if (w.CanStand)
        {
            if (w.HotCoals)
            {
                var events = ApplyCoals(ref s, i);
                s.Mode = MovementMode.Normal;
                s.SmallHitbox = false;
                return new(s, Vector2.Zero, events | MovementEvent.DuckEnded);
            }
            if (i.JumpPressed && s.JumpGrace > 0)
            {
                ApplyJump(ref s, i, w.JumpPad, false, 0, drive);
                s.Mode = MovementMode.Normal;
                s.SmallHitbox = false;
                return new(s, Vector2.Zero, MovementEvent.Jump | MovementEvent.DuckEnded);
            }
            if (!w.Grounded || i.MoveY != 1)
            {
                s.Mode = MovementMode.Normal;
                s.SmallHitbox = false;
                return new(s, Vector2.Zero, MovementEvent.DuckEnded);
            }
        }
        var speed = s.Velocity;
        speed.X = Toward(speed.X, w.CanStand ? 0 : .8f * i.MoveX, .4f * dt);
        if (!w.Grounded) speed.Y = Toward(speed.Y, 2.8f, .3f * dt);
        s.Velocity = speed;
        if (drive == -s.Facing) s.Facing = drive;
        return new(s, speed * dt, MovementEvent.None);
    }

    private static MovementResult AdvanceLedge(MovementState s, MovementInput i,
        MovementEnvironment w, int drive)
    {
        if (CanStartDodge(s, i, w)) return StartDodge(s, i, w, drive, MovementEvent.None);
        if (i.MoveY >= 1 || drive != s.Facing)
        {
            s.LedgeAwayDirection = -s.Facing;
            s.JumpGrace = 12;
            s.Mode = MovementMode.Normal;
            return new(s, Vector2.Zero, MovementEvent.LedgeReleased);
        }
        if (i.JumpPressed)
        {
            ApplyJump(ref s, i, false, false, 0, drive);
            s.Mode = MovementMode.Normal;
            return new(s, Vector2.Zero, MovementEvent.Jump | MovementEvent.LedgeReleased);
        }
        if (!w.LedgeStillValid) s.Mode = MovementMode.Normal;
        return new(s, Vector2.Zero, MovementEvent.None);
    }

    private static void ApplyJump(ref MovementState s, MovementInput i, bool pad,
        bool doubleJump, int ledgeDirection, int drive)
    {
        ClearJumpRequests(ref s);
        var speed = s.Velocity;
        speed.Y = pad ? -4.3f : -3.2f;
        if (ledgeDirection != 0)
        {
            s.Facing = ledgeDirection;
            speed.X = ledgeDirection * 1.5f;
            s.ForcedDirection = ledgeDirection;
            s.ForcedFrames = 6;
        }
        if (drive != 0 && !i.Aiming) speed.X += (doubleJump ? 1 : .5f) * drive;
        if (drive != 0 && doubleJump && (Math.Sign(speed.X) != drive || Math.Abs(speed.X) < 2))
            speed.X = 2 * drive;
        s.Velocity = speed;
        s.VariableJump = true;
    }

    private static void ApplyWallJump(ref MovementState s, int away)
    {
        ClearJumpRequests(ref s);
        s.Velocity = new Vector2(2 * away, -3.2f);
        s.VariableJump = true;
        s.WallFallCap = .5f;
        s.FlapGravity = 1;
        s.Facing = away;
        s.ForcedDirection = away;
        s.ForcedFrames = 12;
    }

    private static MovementEvent ApplyCoals(ref MovementState s, MovementInput i)
    {
        if (s.Velocity.Y < 0) return MovementEvent.None;
        s.Velocity = new Vector2(Math.Clamp(s.Velocity.X + i.MoveX * .3f, -3, 3),
            i.JumpHeld ? -2.8799999f : -2.24f);
        s.AutoBounce = true;
        s.VariableJump = true;
        s.JumpGrace = 0;
        return MovementEvent.CoalBounce;
    }

    private static void ClearJumpRequests(ref MovementState s)
    {
        s.AutoBounce = false;
        s.JumpBuffer = 0;
        s.JumpGrace = 0;
    }

    /// <summary>Host collision hook, invoked using the velocity at impact.</summary>
    public static MovementState CollideVertical(MovementState state)
    {
        var velocity = state.Velocity;
        if (velocity.Y > 0) velocity.X *= 1 - .6f * (velocity.Y / 2.8f);
        else if (state.FlapGravity < 1 && velocity.Y < 0)
        {
            velocity.Y = 1.5f;
            state.Velocity = velocity;
            state.RoofBounceLock = 4;
            return state;
        }
        velocity.Y = 0;
        state.Velocity = velocity;
        return state;
    }

    public static MovementState CollideHorizontal(MovementState state)
    {
        state.Velocity = state.Velocity with { X = 0 };
        return state;
    }

    public static MovementState GrabLedge(MovementState state, int towardWall)
    {
        state.Mode = MovementMode.Ledge;
        state.Facing = towardWall < 0 ? -1 : 1;
        state.Velocity = Vector2.Zero;
        state.FlapGravity = 1;
        return state;
    }

    private static Vector2 EightWay(Vector2 aim)
    {
        float angle = MathF.Floor((MathF.Atan2(aim.Y, aim.X) + MathF.PI / 8) / (MathF.PI / 4))
            * (MathF.PI / 4);
        return new(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static float Toward(float value, float target, float maximumChange) =>
        value < target ? Math.Min(value + maximumChange, target) : Math.Max(value - maximumChange, target);
}
