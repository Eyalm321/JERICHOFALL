using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jerichofall;

public enum Hero { David, Moses, Samson, Joshua }
public enum EnemyKind { Guard, Scorpion, Cobra, Raider, Captain, Sentinel }
public enum Equipment { Stone, Staff, Jawbone, Spear, EmberVessel, BronzeDisc, StormJavelin, Manna, CovenantSeal, WindSandals }
public enum BattleMode { Campaign, Versus }
public enum BattlePhase { Arrival, Fighting, Intermission, Victory, Defeat, RoundOver, MatchOver }
[Flags] public enum Buttons { None=0, Jump=1, Fire=2, Dodge=4, Cycle=8, Pause=16, Confirm=32 }
public readonly record struct PlayerInput(int X=0, int Y=0, Buttons Buttons=Buttons.None)
{
    public bool Has(Buttons value) => (Buttons & value) != 0;
    public void Validate()
    {
        if(X is < -1 or > 1 || Y is < -1 or > 1 || ((int)Buttons & ~63)!=0) throw new InvalidDataException("Invalid player input.");
    }
}

public sealed class ArenaLayout
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string[] Solids { get; set; } = [];
    public string[] Background { get; set; } = [];
    public Vector2[] Spawns { get; set; } = [];
    public Vector2[] Portals { get; set; } = [];
    public Dictionary<string,int[]> Tiles { get; set; } = [];
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Id) || Solids.Length!=24 || Background.Length!=24 || Solids.Concat(Background).Any(r=>r.Length!=32 || r.Any(c=>c is not ('0' or '1')))) throw new InvalidDataException("Invalid arena grid.");
        if(Spawns.Length<4 || Spawns.Any(p=>!Finite(p) || p.X<0 || p.X>=320 || p.Y<0 || p.Y>240 || Blocked(p))) throw new InvalidDataException("Invalid arena spawns.");
        if(Portals.Any(p=>!Finite(p) || p.X<0 || p.X>=320 || p.Y<0 || p.Y>240)) throw new InvalidDataException("Invalid portals.");
    }
    public bool Cell(int x,int y) => Solids[Mod(y,24)][Mod(x,32)]=='1';
    public bool Blocked(Vector2 feet,float width=8,float height=14)
    {
        for(int y=(int)MathF.Floor((feet.Y-height)/10);y<=(int)MathF.Floor((feet.Y-.01f)/10);y++)
        for(int x=(int)MathF.Floor((feet.X-width/2)/10);x<=(int)MathF.Floor((feet.X+width/2-.01f)/10);x++) if(Cell(x,y)) return true;
        return false;
    }
    public bool Point(Vector2 p)=>Cell((int)MathF.Floor(p.X/10),(int)MathF.Floor(p.Y/10));
    public static Vector2 Wrap(Vector2 p)=>new(Mod(p.X,320),Mod(p.Y,240));
    public static Vector2 Delta(Vector2 from,Vector2 to)
    {
        var d=to-from; if(d.X>160)d.X-=320; if(d.X< -160)d.X+=320; if(d.Y>120)d.Y-=240; if(d.Y< -120)d.Y+=240; return d;
    }
    public static bool Finite(Vector2 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y);
    private static int Mod(int v,int n)=>(v%n+n)%n;
    private static float Mod(float v,float n)=>(v%n+n)%n;
}

public sealed class Fighter
{
    public int Id { get; set; }
    public int Slot { get; set; } = -1;
    public Hero Hero { get; set; }
    public EnemyKind Enemy { get; set; }
    public EnemyBrain Brain { get; set; }=new();
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public Motion.MovementState Motion { get; set; }=new();
    public Vector2 PixelRemainder { get; set; }
    public int Facing { get; set; }=1;
    public int Health { get; set; }=1;
    public int Lives { get; set; }=3;
    public int Respawn { get; set; }
    public int Grace { get; set; }
    public int JumpBuffer { get; set; }
    public int Dodge { get; set; }
    public int DodgeCooldown { get; set; }
    public int Invincible { get; set; }
    public int Shield { get; set; }
    public int ShieldRecharge { get; set; }
    public int ShieldReserve { get; set; }
    public int Sandals { get; set; }
    public int Cooldown { get; set; }
    public int Tell { get; set; }
    public bool Grounded { get; set; }
    public bool Aiming { get; set; }
    public DeathSpirit? Spirit { get; set; }
    public float Aim { get; set; }
    public Buttons Previous { get; set; }
    public List<Equipment> Ammo { get; set; }=[];
    [JsonIgnore] public bool IsPlayer=>Slot>=0;
    [JsonIgnore] public bool Alive=>Health>0;
    [JsonIgnore] public Vector2 Center=>Position-new Vector2(0,8);
}
public sealed class Missile
{
    public int Id { get; set; }
    public int Owner { get; set; }
    public Equipment Kind { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public int Age { get; set; }
    public int Bounces { get; set; }
    public EnemyProjectileKind EnemyAttack { get; set; }
    public Vector2 PixelRemainder { get; set; }
    public float Direction { get; set; }
    public bool Falling { get; set; }
    public bool Returning { get; set; }
    public bool Chained { get; set; }
    public int OwnerCatchDelay { get; set; }
    public bool OwnerProtected { get; set; }=true;
    public int PickupDelay { get; set; }
    public long BornFrame { get; set; }=-1;
    public bool Resting { get; set; }
    public bool Removed { get; set; }
}
public sealed class EnemyArrival
{
    public EnemyKind Kind { get; set; }
    public Vector2 Position { get; set; }
    public int Delay { get; set; }
    public int Facing { get; set; }
}
public sealed class Supply
{
    public int Id { get; set; }
    public Equipment Kind { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Start { get; set; }
    public Vector2 Destination { get; set; }
    public int Age { get; set; }
    public bool Animated { get; set; }
    public bool Removed { get; set; }
}
public sealed record GameEvent(string Kind, Vector2 Position, int Actor=-1, int Value=0);
public sealed class BattleState
{
    public long Frame { get; set; }
    public long EffectFrame { get; set; }
    public BattleMode Mode { get; set; }
    public BattlePhase Phase { get; set; }=BattlePhase.Arrival;
    public string Arena { get; set; }="";
    public int Chapter { get; set; }
    public int Wave { get; set; }=-1;
    public int CompletedWaves { get; set; }
    public int Round { get; set; }
    public int Delay { get; set; }=90;
    public int Winner { get; set; }=-1;
    public bool Paused { get; set; }
    public int[] Scores { get; set; }=new int[4];
    public List<Fighter> Fighters { get; set; }=[];
    public List<Missile> Missiles { get; set; }=[];
    public List<Supply> Supplies { get; set; }=[];
    public List<EnemyArrival> Arrivals { get; set; }=[];
    public List<GameEvent> Events { get; set; }=[];
}
public static class GameJson
{
    public static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,IncludeFields=true};
    public static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Options)??throw new InvalidDataException(path);
}
