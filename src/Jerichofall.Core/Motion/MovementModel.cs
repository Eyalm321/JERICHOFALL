using System.Numerics;

namespace Jerichofall.Motion;

public enum MovementMode { Normal, Ledge, Duck, Dodge, Frozen }

[Flags]
public enum MovementEvent
{
    None = 0, Jump = 1, WallJump = 2, DoubleJump = 4, WingFlap = 8,
    DodgeStarted = 16, DodgeEnded = 32, DuckStarted = 64, DuckEnded = 128,
    LedgeReleased = 256, DropThrough = 512, CoalBounce = 1024,
    WallSlide = 2048, Glide = 4096, FastFall = 8192
}

/// <summary>Velocities are pixels per nominal 60 Hz frame; positive Y points down.</summary>
public record struct MovementState
{
    public MovementState() { }
    public MovementMode Mode { get; set; }
    public Vector2 Velocity { get; set; }
    public int Facing { get; set; } = 1;
    public float JumpGrace { get; set; }
    public float JumpBuffer { get; set; }
    public bool VariableJump { get; set; }
    public bool DoubleJumpAvailable { get; set; }
    public bool AutoBounce { get; set; }
    public int ForcedDirection { get; set; }
    public float ForcedFrames { get; set; }
    public int LedgeAwayDirection { get; set; }
    public float WallFallCap { get; set; } = .5f;
    public float FlapGravity { get; set; } = 1;
    public float RoofBounceLock { get; set; }
    public float SlipperyControl { get; set; } = 1;
    public float DodgeFrames { get; set; }
    public float DodgeHoldFrames { get; set; }
    public float DodgeCooldown { get; set; }
    public float CatchPauseFrames { get; set; }
    public bool DodgeSlide { get; set; }
    public bool SmallHitbox { get; set; }
    public bool StartAimingDown { get; set; }
    public int ClingDirection { get; set; }
    public bool Gliding { get; set; }
}

/// <summary>Axes are digital -1, 0, 1; aim can be analog and is quantized for dodges.</summary>
public readonly record struct MovementInput(
    int MoveX = 0, int MoveY = 0,
    bool JumpPressed = false, bool JumpHeld = false,
    bool DodgePressed = false, bool DodgeHeld = false,
    bool Aiming = false, Vector2 Aim = default);

/// <summary>
/// World facts sampled by the host before Step. Wall probes mean a solid at a two-pixel
/// side offset; SolidFiveBelow means a solid at a five-pixel downward offset.
/// Geometry, equipment, and match variants remain external to the movement rules.
/// </summary>
public record struct MovementEnvironment
{
    public MovementEnvironment() { }
    public bool Grounded { get; init; }
    public bool WallLeft { get; init; }
    public bool WallRight { get; init; }
    public bool SolidFiveBelow { get; init; }
    public bool CanStand { get; init; } = true;
    public bool JumpThroughBelow { get; init; }
    public bool ClearThreeBelow { get; init; } = true;
    public bool InsideJumpThrough { get; init; }
    public bool JumpThroughFiveAbove { get; init; }
    public bool InMud { get; init; }
    public bool SlipperyGround { get; init; }
    public bool HotCoals { get; init; }
    public bool JumpPad { get; init; }
    public bool Wings { get; init; }
    public bool SpeedBoots { get; init; }
    public bool DoubleJumpVariant { get; init; }
    public bool EncumbranceVariant { get; init; }
    public int ArrowCount { get; init; }
    public bool NoDodgeVariant { get; init; }
    public bool SuperDodgeVariant { get; init; }
    public bool LedgeStillValid { get; init; } = true;
}

public readonly record struct MovementResult(
    MovementState State, Vector2 Displacement, MovementEvent Events);
