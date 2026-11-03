using System.Numerics;

namespace Jerichofall.Motion;

/// <summary>
/// Integrates our behavioral movement model with authored arena geometry. The
/// public fighter position remains feet; simulation origin is eight pixels above.
/// The controller depends only on the simulation model.
/// </summary>
public sealed class PlayerMotor(ArenaLayout arena, Fighter player) : IDodgeCollisionGeometry, ILedgeGeometry
{
    private MovementState state;
    private Vector2 position, remainder;
    private bool grounded;
    private MovementEvent events;

    public MovementEvent Step(PlayerInput input)
    {
        events=MovementEvent.None;
        state=player.Motion;state.Velocity=player.Velocity;state.Facing=player.Facing;
        position=player.Position;remainder=player.PixelRemainder;
        // Combat fixtures and externally applied catch windows use the public flag.
        if(player.Dodge>0&&state.Mode!=MovementMode.Dodge)
        {state.Mode=MovementMode.Dodge;state.DodgeFrames=player.Dodge;}
        grounded=Blocked(position+Vector2.UnitY,state.SmallHitbox);
        var world=new MovementEnvironment
        {
            Grounded=grounded,WallLeft=Blocked(position-new Vector2(2,0),state.SmallHitbox),
            WallRight=Blocked(position+new Vector2(2,0),state.SmallHitbox),
            SolidFiveBelow=Blocked(position+new Vector2(0,5),state.SmallHitbox),
            CanStand=!Blocked(position,false),SpeedBoots=player.Sandals>0,ArrowCount=player.Ammo.Count,
            LedgeStillValid=state.Mode!=MovementMode.Ledge||LedgeProbe.CanGrabAt(position-new Vector2(0,8),(int)position.Y-10,state.Facing,this)
        };
        var edge=input.Buttons&~player.Previous;
        var controls=new MovementInput(input.X,input.Y,(edge&Buttons.Jump)!=0,input.Has(Buttons.Jump),
            (edge&Buttons.Dodge)!=0,input.Has(Buttons.Dodge),player.Aiming,new(input.X,input.Y));
        var result=MovementKernel.Step(state,controls,world,1,Resolve);
        state=result.State;events|=result.Events;
        // A ledge is a geometry contact, not a free additional jump. Query only
        // while falling into the wall and when the floor probe is clear.
        if(state.Mode==MovementMode.Normal&&state.Velocity.Y>=0&&!player.Aiming&&input.X!=0&&input.Y<=0
            &&Blocked(position+new Vector2(input.X*2,0),false)
            &&!Blocked(position+new Vector2(0,5),false)
            &&LedgeProbe.TryFind(position-new Vector2(0,8),input.X,this,out var ledge))
        {
            state=MovementKernel.GrabLedge(state,input.X);
            position.Y=ledge.BodyY+8;
            for(int i=0;i<2&&!Blocked(position+new Vector2(input.X,0),false);i++)position.X+=input.X;
        }
        player.Position=ArenaLayout.Wrap(position);player.PixelRemainder=remainder;
        player.Motion=state;player.Velocity=state.Velocity;player.Facing=state.Facing;
        player.Grounded=grounded;player.Grace=(int)Math.Max(0,state.JumpGrace);player.JumpBuffer=(int)Math.Max(0,state.JumpBuffer);
        player.Dodge=state.Mode==MovementMode.Dodge?Math.Max(1,(int)state.DodgeFrames):0;
        player.DodgeCooldown=(int)state.DodgeCooldown;
        return events;
    }

    private MovementState Resolve(MovementState next,Vector2 displacement)
    {
        state=next;
        if(state.Mode==MovementMode.Dodge&&displacement!=Vector2.Zero)
        {
            // Adjacent contact is probed on the next frame before movement. This
            // permits the two-pixel corner nudge without moving it a frame early.
            if(state.Velocity.X!=0&&Blocked(position+new Vector2(Math.Sign(state.Velocity.X),0),state.SmallHitbox))
                ApplyResponse(DodgeCollision.Horizontal(state,this,grounded,false));
            if(state.Velocity.Y!=0&&Blocked(position+new Vector2(0,Math.Sign(state.Velocity.Y)),state.SmallHitbox))
                ApplyResponse(DodgeCollision.Vertical(state,this,false));
            displacement=state.Velocity;
        }
        MoveAxis(displacement.X,true,true);MoveAxis(displacement.Y,false,true);
        return state;
    }

    private void MoveAxis(float delta,bool horizontal,bool respond)
    {
        float value=(horizontal?remainder.X:remainder.Y)+delta;
        int pixels=(int)Math.Round(value,MidpointRounding.ToEven);
        if(horizontal)remainder.X=value-pixels;else remainder.Y=value-pixels;
        Vector2 step=horizontal?new(Math.Sign(pixels),0):new(0,Math.Sign(pixels));
        for(int left=Math.Abs(pixels);left>0;left--)
        {
            if(!Blocked(position+step,state.SmallHitbox)){position+=step;continue;}
            if(horizontal)remainder.X=0;else remainder.Y=0;
            if(!respond)break;
            if(state.Mode==MovementMode.Dodge)
            {
                var response=horizontal?DodgeCollision.Horizontal(state,this,grounded,true):DodgeCollision.Vertical(state,this,true);
                ApplyResponse(response);
            }
            else
            {
                state=horizontal?MovementKernel.CollideHorizontal(state):MovementKernel.CollideVertical(state);
            }
            break;
        }
    }

    private void ApplyResponse(DodgeCollisionResult response)
    {
        state=response.State;position+=response.DirectPositionChange;
        if(response.Nudge!=Vector2.Zero){MoveAxis(response.Nudge.X,true,false);MoveAxis(response.Nudge.Y,false,false);}
    }
    private bool Blocked(Vector2 feet,bool small)=>arena.Blocked(feet,8,small?6:14);
    bool IDodgeCollisionGeometry.SolidAtOffset(Vector2 offset,bool smallBody)=>Blocked(position+offset,smallBody);
    bool IDodgeCollisionGeometry.EnteringOneWayAtOffset(Vector2 offset,bool smallBody)=>false;
    bool ILedgeGeometry.BodyIntersectsSolid(Vector2 origin)=>Blocked(origin+new Vector2(0,8),false);
    bool ILedgeGeometry.SolidContainsPoint(Vector2 point)=>arena.Point(point);
}
