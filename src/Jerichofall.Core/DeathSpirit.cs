using System.Numerics;
using System.Text.Json.Serialization;

namespace Jerichofall;

public enum SpiritPhase { Waiting, Emerging, Drifting, Dashing, Dispersing, Gone }

/// <summary>Persistent death state: snapshots retain it even if the death event was skipped.</summary>
public sealed class DeathSpirit
{
    public SpiritPhase Phase { get; set; }
    public int Age { get; set; }
    public int PhaseAge { get; set; }
    public int Warmup { get; set; }=30;
    public int Cooldown { get; set; }
    public int Facing { get; set; }=1;
    public int CorpseFacing { get; set; }=1;
    public int Happy { get; set; }
    public bool Playable { get; set; }
    public bool Banished { get; set; }
    public Vector2 Corpse { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }=new(0,-2);
    public Vector2 Remainder { get; set; }
    public Vector2 LastInput { get; set; }
    public float CruiseSpeed { get; set; }=1.6f;
    [JsonIgnore] public bool Tangible=>Playable&&Phase is SpiritPhase.Drifting or SpiritPhase.Dashing;
    [JsonIgnore] public bool Visible=>Phase is not (SpiritPhase.Waiting or SpiritPhase.Gone);
}

/// <summary>Independently implemented spirit motion, covered by recorded input regression tests.</summary>
public static class SpiritMotor
{
    // Includes the observed scene/coroutine scheduling at nominal 60 Hz.
    public const int ReturnDelay=62, EmergenceFrames=32, DashFrames=12, FadeFrames=30;
    public static DeathSpirit Create(Fighter fighter,bool playable)=>new()
    {
        Phase=playable?SpiritPhase.Waiting:SpiritPhase.Emerging,Playable=playable,
        Corpse=fighter.Position,Position=ArenaLayout.Wrap(fighter.Position-new Vector2(0,4)),Facing=fighter.Facing,CorpseFacing=fighter.Facing
    };
    public static void Disperse(DeathSpirit spirit,bool banished=false)
    {
        if(spirit.Phase is SpiritPhase.Dispersing or SpiritPhase.Gone)return;
        spirit.Phase=SpiritPhase.Dispersing;spirit.PhaseAge=0;spirit.Banished=banished;
        spirit.Velocity=banished?Vector2.Zero:new(0,-.65f);
    }
    public static void Step(DeathSpirit spirit,ArenaLayout layout,PlayerInput input,Buttons previous)
    {
        if(spirit.Phase==SpiritPhase.Gone)return;
        spirit.Age++;spirit.Happy=Math.Max(0,spirit.Happy-1);
        if(spirit.Phase==SpiritPhase.Waiting)
        {
            if(++spirit.PhaseAge<=ReturnDelay)return;
            spirit.Phase=SpiritPhase.Emerging;spirit.PhaseAge=0;
        }
        var direction=new Vector2(input.X,input.Y);
        if(spirit.Phase==SpiritPhase.Dispersing)
        {
            spirit.Position=ArenaLayout.Wrap(spirit.Position+spirit.Velocity);
            if(++spirit.PhaseAge>=FadeFrames)spirit.Phase=SpiritPhase.Gone;
            return;
        }
        if(spirit.Phase==SpiritPhase.Emerging)
        {
            if(spirit.PhaseAge<EmergenceFrames-1)spirit.Velocity=Approach(spirit.Velocity,Vector2.Zero,.05f);
            if(spirit.Playable)Move(spirit,layout,direction*.8f,false);
            Move(spirit,layout,spirit.Velocity,false);
            if(++spirit.PhaseAge>=EmergenceFrames){spirit.Phase=SpiritPhase.Drifting;spirit.PhaseAge=0;spirit.Warmup--;}
            return;
        }
        spirit.Warmup=Math.Max(0,spirit.Warmup-1);
        if(spirit.Phase==SpiritPhase.Dashing)
        {
            if(spirit.PhaseAge<DashFrames-1)spirit.Velocity=Approach(spirit.Velocity,Vector2.Zero,.4f);
            Move(spirit,layout,spirit.Velocity,true);
            if(++spirit.PhaseAge>=DashFrames){spirit.Phase=SpiritPhase.Drifting;spirit.PhaseAge=0;spirit.Cooldown=30;}
            return;
        }
        if(!spirit.Playable)direction=Vector2.Zero;
        spirit.CruiseSpeed=direction!=Vector2.Zero&&direction==spirit.LastInput&&spirit.Velocity.LengthSquared()>=spirit.CruiseSpeed*spirit.CruiseSpeed-1
            ?Math.Min(2.8f,spirit.CruiseSpeed+.02f):1.6f;
        spirit.LastInput=direction;
        if(direction.X!=0)spirit.Facing=Math.Sign(direction.X);
        spirit.Velocity=Approach(spirit.Velocity,direction==Vector2.Zero?Vector2.Zero:Vector2.Normalize(direction)*spirit.CruiseSpeed,.3f);
        bool cooling=spirit.Cooldown>0;if(cooling)spirit.Cooldown--;
        if(spirit.Playable&&!cooling&&((input.Buttons&~previous)&(Buttons.Dodge|Buttons.Fire))!=0)
        {
            spirit.Phase=SpiritPhase.Dashing;spirit.PhaseAge=0;
            spirit.Velocity=Vector2.Normalize(direction==Vector2.Zero?new(spirit.Facing,0):direction)*6;
        }
        else
        {
            float tick=spirit.Age-(spirit.Playable?ReturnDelay:0);
            Move(spirit,layout,new(MathF.Sin(tick*MathF.Tau/60)*.2f,MathF.Sin(tick*MathF.PI/60)*.2f),spirit.Playable);
        }
        Move(spirit,layout,spirit.Velocity,spirit.Playable);
    }
    private static Vector2 Approach(Vector2 value,Vector2 target,float step)
    {
        var delta=target-value;float distance=delta.Length();
        return distance<=step?target:value+delta*(step/distance);
    }
    private static void Move(DeathSpirit spirit,ArenaLayout layout,Vector2 velocity,bool collide)
    {
        for(int axis=0;axis<2;axis++)
        {
            float remainder=axis==0?spirit.Remainder.X:spirit.Remainder.Y;
            float amount=remainder+(axis==0?velocity.X:velocity.Y);
            int pixels=(int)MathF.Round(amount,MidpointRounding.ToEven);
            spirit.Remainder=axis==0?new(amount-pixels,spirit.Remainder.Y):new(spirit.Remainder.X,amount-pixels);
            var unit=axis==0?new Vector2(Math.Sign(pixels),0):new(0,Math.Sign(pixels));
            for(int i=0;i<Math.Abs(pixels);i++)
            {
                var next=ArenaLayout.Wrap(spirit.Position+unit);
                if(collide&&layout.Blocked(next+new Vector2(0,4),8,8))
                {
                    float speed=axis==0?spirit.Velocity.X:spirit.Velocity.Y;
                    float bounced=Math.Abs(speed)>2.2f?-speed*.6f:0;
                    spirit.Velocity=axis==0?new(bounced,spirit.Velocity.Y):new(spirit.Velocity.X,bounced);
                    break;
                }
                spirit.Position=next;
            }
        }
    }
}
