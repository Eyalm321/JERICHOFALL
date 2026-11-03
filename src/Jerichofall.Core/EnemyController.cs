using System.Numerics;

namespace Jerichofall;

public enum EnemyPhase { Patrol, Windup, Attack, Recover, Stunned, Dying }
public enum EnemyProjectileKind { None, Arrow, Venom, GroundWave }

public sealed class EnemyBrain
{
    public EnemyPhase Phase { get; set; }
    public int PhaseRemaining { get; set; }
    public int Cooldown { get; set; }
    public int HopCooldown { get; set; } = 40;
    public int Age { get; set; }
    public float Aim { get; set; }
}

public readonly record struct EnemyProfile(int Health, int Width, int Height, float Walk,
    float Range, int Windup, int Attack, int Recovery);

/// <summary>
/// Independent host for the project's previously authored campaign enemy controller.
/// Patrol, warning, attack and recovery behavior match that controller's settings;
/// the caller supplies movement/collision and player damage through the new simulation.
/// </summary>
public static class EnemyController
{
    public static EnemyProfile Profile(EnemyKind kind) => kind switch
    {
        EnemyKind.Guard => new(1, 10, 17, .48f, 77, 32, 20, 34),
        EnemyKind.Scorpion => new(1, 15, 8, .36f, 65, 26, 15, 42),
        EnemyKind.Cobra => new(1, 10, 13, .18f, 145, 48, 1, 88),
        EnemyKind.Raider => new(1, 10, 17, .58f, 148, 44, 1, 76),
        EnemyKind.Captain => new(3, 12, 18, .36f, 83, 46, 25, 54),
        EnemyKind.Sentinel => new(5, 19, 25, .25f, 91, 62, 18, 92),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static Fighter Spawn(int id, EnemyKind kind, Vector2 feet)
    {
        return new Fighter
        {
            Id = id, Enemy = kind, Position = feet, Health = Profile(kind).Health,
            Brain = new EnemyBrain { Cooldown = 25 + Math.Abs((int)feet.X) % 23 }
        };
    }

    public static void Step(Battle battle, Fighter enemy, Action<Fighter> move)
    {
        var brain = enemy.Brain;
        var profile = Profile(enemy.Enemy);
        brain.Age++;
        enemy.Invincible = Math.Max(0, enemy.Invincible - 1);
        brain.Cooldown = Math.Max(0, brain.Cooldown - 1);
        brain.HopCooldown = Math.Max(0, brain.HopCooldown - 1);
        enemy.Velocity = new(enemy.Velocity.X, MathF.Min(3.2f, enemy.Velocity.Y + .2f));
        var target = battle.State.Fighters.Where(p => p.IsPlayer && p.Alive)
            .MinBy(p => ArenaLayout.Delta(enemy.Position, p.Position).LengthSquared());
        var sight = enemy.Position - new Vector2(0, profile.Height * .6f);
        var targetOrigin = target?.Position - new Vector2(0, 8);
        var offset = targetOrigin is { } point ? ArenaLayout.Delta(sight, point) : Vector2.Zero;
        enemy.Grounded = Below(battle.Layout, enemy, profile);

        switch (brain.Phase)
        {
            case EnemyPhase.Patrol:
                Patrol(battle, enemy, profile, target, sight, targetOrigin, offset);
                break;
            case EnemyPhase.Windup:
                enemy.Velocity = new(Approach(enemy.Velocity.X, 0, .25f), enemy.Velocity.Y);
                if (target != null && brain.PhaseRemaining > 28) Aim(enemy, offset);
                if (--brain.PhaseRemaining <= 0) Attack(battle, enemy, profile, sight);
                break;
            case EnemyPhase.Attack:
                if (--brain.PhaseRemaining <= 0 || MathF.Abs(enemy.Velocity.X) < .01f &&
                    enemy.Enemy is EnemyKind.Guard or EnemyKind.Scorpion or EnemyKind.Captain)
                {
                    brain.Phase = EnemyPhase.Recover;
                    brain.PhaseRemaining = profile.Recovery;
                }
                break;
            case EnemyPhase.Recover:
            case EnemyPhase.Stunned:
                enemy.Velocity = new(Approach(enemy.Velocity.X, 0, .1f), enemy.Velocity.Y);
                if (--brain.PhaseRemaining <= 0)
                {
                    brain.Phase = EnemyPhase.Patrol;
                    brain.Cooldown = 25;
                }
                break;
        }
        enemy.Tell = brain.Phase == EnemyPhase.Windup ? brain.PhaseRemaining : 0;
        enemy.Aim = brain.Aim;
        move(enemy);
    }

    private static void Patrol(Battle battle, Fighter enemy, EnemyProfile profile, Fighter? target,
        Vector2 sight, Vector2? targetOrigin, Vector2 offset)
    {
        var brain = enemy.Brain;
        bool visible = targetOrigin is { } point && CombatPhysics.HasLineOfSight(battle.Layout, sight, point);
        if (target != null && MathF.Abs(offset.X) > 4) enemy.Facing = Math.Sign(offset.X);
        float desired = profile.Walk * enemy.Facing;
        if (enemy.Enemy == EnemyKind.Raider && visible)
            desired = MathF.Abs(offset.X) < 55 ? -enemy.Facing * .75f : MathF.Abs(offset.X) < 118 ? 0 : desired;
        if (enemy.Enemy == EnemyKind.Cobra && visible) desired = 0;
        if (enemy.Grounded && !Below(battle.Layout, enemy, profile, enemy.Facing * (profile.Width / 2 + 3)) &&
            enemy.Enemy is EnemyKind.Cobra or EnemyKind.Raider or EnemyKind.Sentinel) desired = 0;
        enemy.Velocity = new(Approach(enemy.Velocity.X, desired, .075f), enemy.Velocity.Y);
        bool ranged = enemy.Enemy is EnemyKind.Cobra or EnemyKind.Raider;
        bool aligned = ranged || MathF.Abs(offset.Y) < (enemy.Enemy == EnemyKind.Sentinel ? 36 : 23);
        if (enemy.Grounded && brain.Cooldown <= 0 && visible && offset.LengthSquared() <= profile.Range * profile.Range && aligned)
        {
            Aim(enemy, offset);
            brain.Phase = EnemyPhase.Windup;
            brain.PhaseRemaining = profile.Windup;
            enemy.Velocity = new(0, enemy.Velocity.Y);
            return;
        }
        if (enemy.Grounded && brain.HopCooldown <= 0 && target != null &&
            enemy.Enemy is EnemyKind.Guard or EnemyKind.Raider or EnemyKind.Captain)
        {
            bool wall = battle.Layout.Blocked(enemy.Position + new Vector2(enemy.Facing * 3, 0), profile.Width, profile.Height);
            if (wall || offset.Y < -22 && MathF.Abs(offset.X) < 72)
            {
                enemy.Velocity = new(enemy.Facing * 1.35f, -4.6f);
                brain.HopCooldown = 90;
            }
        }
    }

    private static void Aim(Fighter enemy, Vector2 offset)
    {
        if (offset.LengthSquared() < .01f) offset = new(enemy.Facing, 0);
        enemy.Brain.Aim = MathF.Atan2(offset.Y, offset.X);
        if (offset.X != 0) enemy.Facing = Math.Sign(offset.X);
    }

    private static void Attack(Battle battle, Fighter enemy, EnemyProfile profile, Vector2 sight)
    {
        enemy.Brain.Phase = EnemyPhase.Attack;
        enemy.Brain.PhaseRemaining = profile.Attack;
        switch (enemy.Enemy)
        {
            case EnemyKind.Guard: enemy.Velocity = new(enemy.Facing * 2.35f, enemy.Velocity.Y); break;
            case EnemyKind.Scorpion: enemy.Velocity = new(enemy.Facing * 3.2f, enemy.Velocity.Y); break;
            case EnemyKind.Captain: enemy.Velocity = new(enemy.Facing * 2.45f, enemy.Velocity.Y); break;
            case EnemyKind.Cobra:
                Launch(battle, enemy, sight, enemy.Brain.Aim, 2.1f, EnemyProjectileKind.Venom);
                break;
            case EnemyKind.Raider:
                Launch(battle, enemy, sight, enemy.Brain.Aim, 4, EnemyProjectileKind.Arrow);
                break;
            case EnemyKind.Sentinel:
                Launch(battle, enemy, enemy.Position + new Vector2(-12, -3), MathF.PI, 2.05f, EnemyProjectileKind.GroundWave);
                Launch(battle, enemy, enemy.Position + new Vector2(12, -3), 0, 2.05f, EnemyProjectileKind.GroundWave);
                break;
        }
    }

    private static void Launch(Battle battle, Fighter enemy, Vector2 position, float angle, float speed, EnemyProjectileKind kind)
    {
        battle.Launch(enemy, Equipment.Stone, angle, speed);
        var missile = battle.State.Missiles[^1];
        missile.Position = ArenaLayout.Wrap(position);
        missile.EnemyAttack = kind;
    }

    public static bool CanContactHurt(Fighter enemy) => enemy.Alive && enemy.Invincible <= 0 &&
        enemy.Brain.Phase != EnemyPhase.Stunned &&
        (enemy.Enemy is EnemyKind.Cobra or EnemyKind.Scorpion || enemy.Brain.Phase == EnemyPhase.Attack);

    public static void OnHit(Fighter enemy, Vector2 force = default)
    {
        enemy.Invincible = 22;
        enemy.Velocity = new(Math.Clamp(force.X * .35f, -2, 2), -1.5f);
        enemy.Brain.Phase = EnemyPhase.Stunned;
        enemy.Brain.PhaseRemaining = 28;
        enemy.Tell = 0;
    }

    private static bool Below(ArenaLayout layout, Fighter enemy, EnemyProfile profile, float offset = 0) =>
        layout.Blocked(enemy.Position + new Vector2(offset, 1), profile.Width, profile.Height);

    private static float Approach(float value, float target, float amount) =>
        value < target ? MathF.Min(target, value + amount) : MathF.Max(target, value - amount);
}
