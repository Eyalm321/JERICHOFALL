using System.Numerics;

namespace Jerichofall;

/// <summary>Project-owned simulation. No engine adapters, reflection, native game types or binary patches.</summary>
public sealed class Battle
{
    public ArenaLayout Layout { get; }
    public BattleState State { get; }=new();
    private readonly Hero[] heroes;
    private readonly EnemyKind[][] waves;
    private int identity=10;
    private int supplyTimer,clearDelay,ghostWait;
    private readonly Dictionary<int,int> portalCounts=[];
    public Battle(ArenaLayout layout,Hero[] players,BattleMode mode,int chapter=0,EnemyKind[][]? encounters=null)
    {
        layout.Validate();
        if(players.Length<(mode==BattleMode.Campaign?1:2)||players.Length>(mode==BattleMode.Campaign?2:4)||players.Any(h=>!Enum.IsDefined(h))) throw new ArgumentException("Invalid roster.");
        if(mode==BattleMode.Campaign && (encounters?.Length!=3 || encounters.Any(w=>w.Length==0 || w.Length>16 || w.Any(e=>!Enum.IsDefined(e))))) throw new ArgumentException("A chapter requires three encounters.");
        Layout=layout;heroes=players.ToArray();waves=encounters??[];
        State.Mode=mode;State.Arena=layout.Id;State.Chapter=chapter;ResetRound();
    }
    public void ResetRound(bool newMatch=false)
    {
        if(newMatch){Array.Clear(State.Scores);State.Round=0;} else State.Round++;
        State.Fighters.Clear();State.Missiles.Clear();State.Supplies.Clear();State.Events.Clear();State.Arrivals.Clear();portalCounts.Clear();clearDelay=0;
        State.Phase=BattlePhase.Arrival;State.Delay=State.Mode==BattleMode.Campaign?100:75;State.Wave=-1;State.CompletedWaves=0;State.Winner=-1;State.Paused=false;ghostWait=90;
        for(int i=0;i<heroes.Length;i++)
        {
            var p=new Fighter{Id=i,Slot=i,Hero=heroes[i],Facing=i%2==0?1:-1};
            Respawn(p);State.Fighters.Add(p);
        }
        AddSupplies(0);supplyTimer=420;
    }
    public static Equipment BaseWeapon(Hero hero)=>(Equipment)(int)hero;
    private void Respawn(Fighter p)
    {
        // Declaration order includes adjacent same-side points; space Versus players across the arena.
        int at=State.Mode==BattleMode.Versus?(p.Slot*Layout.Spawns.Length/heroes.Length)%Layout.Spawns.Length:p.Slot;
        p.Position=Layout.Spawns[at];p.Velocity=Vector2.Zero;p.Health=1;p.Respawn=0;p.Invincible=100;p.Dodge=0;
        p.Motion=new(){Facing=p.Facing};p.PixelRemainder=Vector2.Zero;p.DodgeCooldown=0;
        p.Shield=State.Mode==BattleMode.Campaign?1:0;p.ShieldReserve=0;p.ShieldRecharge=0;p.Sandals=0;p.Cooldown=0;
        p.Previous=Buttons.None;p.Aiming=false;p.Ammo=Enumerable.Repeat(BaseWeapon(p.Hero),3).ToList();
        if(p.Spirit!=null)SpiritMotor.Disperse(p.Spirit);
    }
    public void Step(ReadOnlySpan<PlayerInput> inputs)
    {
        foreach(var input in inputs)input.Validate();
        State.Events.Clear();
        bool pause=false,confirm=false;
        foreach(var p in State.Fighters.Where(p=>p.IsPlayer))
        {
            var input=p.Slot<inputs.Length?inputs[p.Slot]:default;
            pause|=input.Has(Buttons.Pause)&&(p.Previous&Buttons.Pause)==0;
            confirm|=input.Has(Buttons.Confirm)&&(p.Previous&Buttons.Confirm)==0;
        }
        if(pause)State.Paused=!State.Paused;
        if(State.Paused){Remember(inputs);return;}
        State.EffectFrame++;
        AdvanceSpirits(inputs);
        if(State.Phase is BattlePhase.RoundOver or BattlePhase.MatchOver)
        {
            if(confirm)ResetRound(State.Phase==BattlePhase.MatchOver);
            Remember(inputs);return;
        }
        if(State.Phase is BattlePhase.Victory or BattlePhase.Defeat){Remember(inputs);return;}
        State.Frame++;
        foreach(var p in State.Fighters.Where(p=>p.IsPlayer))
        {
            if(!p.Alive)
            {
                if(State.Mode==BattleMode.Campaign&&p.Lives>0&&--p.Respawn<=0)Respawn(p);
                continue;
            }
            AdvancePlayer(p,p.Slot<inputs.Length?inputs[p.Slot]:default);
        }
        AdvanceArrivals();
        foreach(var e in State.Fighters.Where(e=>!e.IsPlayer&&e.Alive).ToArray())AdvanceEnemy(e);
        foreach(var missile in State.Missiles.ToArray())AdvanceMissile(missile);
        foreach(var supply in State.Supplies.Where(s=>!s.Removed))
        {
            supply.Age++;
            if(supply.Age>=900){supply.Removed=true;continue;}
            if(supply.Animated)
            {
                float t=Math.Clamp(supply.Age/24f,0,1);
                supply.Position=Vector2.Lerp(supply.Start,supply.Destination,1-MathF.Pow(1-t,3));
                supply.Position-=new Vector2(0,MathF.Sin(supply.Age*.055f)*1.5f*t);
            }
            if(supply.Age<12)continue;
            foreach(var p in State.Fighters.Where(p=>p.IsPlayer&&p.Alive))
                if(ArenaLayout.Delta(p.Center,supply.Position).LengthSquared()<170&&ApplySupply(p,supply.Kind)){supply.Removed=true;break;}
        }
        ResolveContacts();
        ResolveSpiritContacts();
        State.Missiles.RemoveAll(m=>m.Removed || m.Age>1800);
        State.Supplies.RemoveAll(s=>s.Removed);
        State.Fighters.RemoveAll(e=>!e.IsPlayer&&!e.Alive);
        if(State.Mode==BattleMode.Campaign)AdvanceCampaign(); else AdvanceVersus();
        Remember(inputs);
    }
    private void Remember(ReadOnlySpan<PlayerInput> inputs)
    {
        foreach(var p in State.Fighters.Where(p=>p.IsPlayer))p.Previous=p.Slot<inputs.Length?inputs[p.Slot].Buttons:Buttons.None;
    }
    private bool BattleEnded=>State.Phase is BattlePhase.RoundOver or BattlePhase.MatchOver or BattlePhase.Victory or BattlePhase.Defeat;
    private void AdvanceSpirits(ReadOnlySpan<PlayerInput> inputs)
    {
        foreach(var p in State.Fighters.Where(p=>p.IsPlayer&&p.Spirit!=null))
        {
            var spirit=p.Spirit!;
            if((p.Alive||BattleEnded&&spirit.Age>=100)&&spirit.Phase!=SpiritPhase.Gone)SpiritMotor.Disperse(spirit);
            var input=!BattleEnded&&p.Slot<inputs.Length?inputs[p.Slot]:default;
            var before=spirit.Phase;SpiritMotor.Step(spirit,Layout,input,p.Previous);
            if(before==SpiritPhase.Waiting&&spirit.Visible)State.Events.Add(new("spirit-rise",spirit.Position,p.Id));
            if(before!=SpiritPhase.Dashing&&spirit.Phase==SpiritPhase.Dashing)State.Events.Add(new("spirit-dash",spirit.Position,p.Id));
        }
    }
    private void Banish(DeathSpirit spirit,int owner)
    {
        SpiritMotor.Disperse(spirit,true);State.Events.Add(new("spirit-banish",spirit.Position,owner));
    }
    private void ResolveSpiritContacts()
    {
        if(State.Mode!=BattleMode.Versus||BattleEnded)return;
        foreach(var owner in State.Fighters.Where(p=>p.Spirit?.Tangible==true).ToArray())
        {
            var spirit=owner.Spirit!;
            foreach(var p in State.Fighters.Where(p=>p.Alive&&p.Id!=owner.Id).ToArray())
            {
                var delta=ArenaLayout.Delta(spirit.Position,p.Center);
                if(Math.Abs(delta.X)>=8||Math.Abs(delta.Y)>=11)continue;
                if(p.Velocity.Y>0&&ArenaLayout.Delta(spirit.Position,p.Position).Y<=2)
                {
                    Banish(spirit,p.Id);p.Velocity=new(p.Velocity.X,-3.2f);
                    var motion=p.Motion;motion.Velocity=p.Velocity;motion.VariableJump=true;p.Motion=motion;break;
                }
                if(spirit.Warmup==0&&Damage(p,owner.Id))spirit.Happy=30;
                spirit.Velocity=delta.LengthSquared()<.01f?new(-spirit.Facing*3,0):-Vector2.Normalize(delta)*3;
            }
        }
    }
    private void AdvancePlayer(Fighter p,PlayerInput input)
    {
        var edge=input.Buttons&~p.Previous;
        p.Invincible=Math.Max(0,p.Invincible-1);p.Cooldown=Math.Max(0,p.Cooldown-1);
        p.Sandals=Math.Max(0,p.Sandals-1);
        if(p.ShieldRecharge>0&&--p.ShieldRecharge==0)
        {p.Shield=1;p.ShieldReserve=0;Emit("seal-recharge",p);}
        if(input.X!=0||input.Y!=0)p.Aim=MathF.Atan2(input.Y,input.X);
        else if(!p.Aiming)p.Aim=p.Facing<0?MathF.PI:0;
        if((edge&Buttons.Cycle)!=0&&p.Ammo.Count>1)
        {var first=p.Ammo[0];p.Ammo.RemoveAt(0);p.Ammo.Add(first);}
        // Input enters the aim state after the movement update; release also
        // completes the current aim frame before the next movement frame.
        bool grounded=p.Grounded;
        var firingPosition=p.Position-new Vector2(0,9);
        var motion=new Motion.PlayerMotor(Layout,p).Step(input);
        if((motion&(Motion.MovementEvent.Jump|Motion.MovementEvent.WallJump))!=0)Emit("jump",p);
        if(!grounded&&p.Grounded)Emit("land",p);
        if((motion&Motion.MovementEvent.DodgeStarted)!=0){p.Aiming=false;Emit("dodge",p);}
        if((edge&Buttons.Fire)!=0&&p.Dodge==0)p.Aiming=true;
        if(p.Aiming&&!input.Has(Buttons.Fire))
        {
            p.Aiming=false;
            if(p.Ammo.Count>0&&p.Cooldown==0)
            {var kind=p.Ammo[0];p.Ammo.RemoveAt(0);Launch(p,kind,p.Aim);State.Missiles[^1].Position=firingPosition;p.Cooldown=8;}
            else Emit("empty",p);
        }
    }
    private void AdvanceEnemy(Fighter e)=>EnemyController.Step(this,e,Move);
    private void Move(Fighter p)
    {
        var profile=EnemyController.Profile(p.Enemy);
        for(int axis=0;axis<2;axis++)
        {
            float distance=(axis==0?p.PixelRemainder.X:p.PixelRemainder.Y)+(axis==0?p.Velocity.X:p.Velocity.Y);
            int pixels=(int)Math.Round(distance,MidpointRounding.ToEven);
            p.PixelRemainder=axis==0?new(distance-pixels,p.PixelRemainder.Y):new(p.PixelRemainder.X,distance-pixels);
            Vector2 step=axis==0?new(Math.Sign(pixels),0):new(0,Math.Sign(pixels));
            for(int i=0;i<Math.Abs(pixels);i++)
            {
                var next=ArenaLayout.Wrap(p.Position+step);
                if(Layout.Blocked(next,profile.Width,profile.Height))
                {
                    if(axis==0){p.Velocity=new(0,p.Velocity.Y);p.PixelRemainder=new(0,p.PixelRemainder.Y);}
                    else {p.Velocity=new(p.Velocity.X,0);p.PixelRemainder=new(p.PixelRemainder.X,0);}
                    break;
                }
                p.Position=next;
            }
        }
    }
    public void Launch(Fighter owner,Equipment kind,float angle,float? speedOverride=null)
    {
        State.Missiles.Add(new(){Id=identity++,Owner=owner.Id,Kind=kind,
            Position=owner.Position-new Vector2(0,9),Velocity=CombatPhysics.LaunchVelocity(kind,angle,speedOverride),
            Direction=angle,BornFrame=State.Frame});
        State.Events.Add(new("shoot",owner.Center,owner.Id,(int)kind));
    }
    private bool Collect(Missile m,Fighter p,string sound="ammo-pickup")
    {
        if(!p.IsPlayer||p.Ammo.Count>=CombatPhysics.AmmoCapacity)return false;
        // Base ammunition adopts the collecting hero's weapon, as in the native mod.
        p.Ammo.Add(m.Kind<=Equipment.Spear?BaseWeapon(p.Hero):m.Kind);
        m.Removed=true;Emit(sound,p);return true;
    }
    private void AdvanceMissile(Missile m)
    {
        if(m.Removed||m.BornFrame==State.Frame)return;
        m.Age++;m.PickupDelay=Math.Max(0,m.PickupDelay-1);m.OwnerCatchDelay=Math.Max(0,m.OwnerCatchDelay-1);
        var owner=State.Fighters.Find(p=>p.Id==m.Owner);
        bool hazard=m.EnemyAttack is EnemyProjectileKind.Venom or EnemyProjectileKind.GroundWave;
        if(m.Resting)
        {
            if(m.PickupDelay==0)
                foreach(var p in State.Fighters.Where(p=>p.IsPlayer&&p.Alive))
                    if(Overlaps(m,p)&&Collect(m,p))break;
            return;
        }
        if(hazard)
        {
            bool ground=m.EnemyAttack==EnemyProjectileKind.GroundWave;
            if(m.Age>=(ground?48:95)||ground&&!Layout.Point(ArenaLayout.Wrap(m.Position+new Vector2(Math.Sign(m.Velocity.X)*5,5))))
            {m.Removed=true;return;}
            if(!ground)m.Velocity+=new Vector2(0,.012f);
        }
        else
        {
            Vector2? ownerDelta=owner is {Alive:true}?ArenaLayout.Delta(m.Position,owner.Position-new Vector2(0,8)):null;
            if(m.Kind==Equipment.BronzeDisc&&(m.Returning||m.Age>=19)&&owner is {Alive:true}&&
                ownerDelta?.LengthSquared()<=CombatPhysics.DiscReturnDistanceSquared&&Collect(m,owner,"disc-return"))return;
            var flight=m.EnemyAttack==EnemyProjectileKind.Arrow?
                CombatPhysics.AdvanceArrow(m.Velocity,m.Age,m.Direction,m.Falling):
                CombatPhysics.AdvanceFlight(m.Kind,m.Velocity,m.Age,m.Direction,m.Falling,m.Returning,ownerDelta);
            m.Velocity=flight.Velocity;m.Direction=flight.Direction;m.Returning=flight.Returning;
            if(!m.Falling&&flight.Falling)m.OwnerProtected=false;
            m.Falling=flight.Falling;
            if(flight.Detonate){Blast(m,CombatPhysics.EmberRadius);return;}
        }
        MoveMissileAxis(m,true);if(!m.Removed&&!m.Resting)MoveMissileAxis(m,false);
        if(m.Removed||m.Resting)return;
        if(owner!=null&&ArenaLayout.Delta(m.Position,owner.Position-new Vector2(0,8)).LengthSquared()>=CombatPhysics.OwnerProtectionDistanceSquared)
            m.OwnerProtected=false;
        if(!hazard)
            foreach(var venom in State.Missiles.Where(v=>!v.Removed&&v.EnemyAttack is EnemyProjectileKind.Venom or EnemyProjectileKind.GroundWave))
                if(owner is {IsPlayer:true}&&CombatPhysics.Overlaps(m.Kind,m.Position,venom.Position,4,4))venom.Removed=true;
        if(!hazard&&State.Mode==BattleMode.Versus&&CombatPhysics.IsDangerous(m.Velocity,m.Direction,m.Falling))
        {
            var spiritOwner=State.Fighters.FirstOrDefault(p=>p.Spirit?.Tangible==true&&
                CombatPhysics.Overlaps(m.Kind,m.Position,p.Spirit.Position,12,12));
            if(spiritOwner?.Spirit is{} spirit)
            {
                Banish(spirit,m.Owner);m.Falling=true;m.OwnerProtected=false;m.Velocity=new(-Math.Sign(m.Velocity.X)*.65f,-2.5f);
                m.PixelRemainder=Vector2.Zero;return;
            }
        }
        foreach(var target in State.Fighters.Where(p=>p.Alive).ToArray())
        {
            if(!Overlaps(m,target)||target.Id==m.Owner&&(m.OwnerProtected||m.Kind==Equipment.BronzeDisc))continue;
            if(owner is {IsPlayer:false}&&!target.IsPlayer)continue;
            if(State.Mode==BattleMode.Campaign&&owner is {IsPlayer:true}&&target.IsPlayer&&target.Id!=owner.Id)continue;
            bool dangerous=hazard||CombatPhysics.IsDangerous(m.Velocity,m.Direction,m.Falling);
            if(target.IsPlayer&&target.Dodge>0&&!hazard&&target.Shield==0&&(target.Id!=m.Owner||m.OwnerCatchDelay==0))
            {
                if(!Collect(m,target))
                {
                    m.Falling=true;m.OwnerProtected=false;m.OwnerCatchDelay=CombatPhysics.DroppedOwnerCatchDelay;
                    m.Velocity=new(target.Facing*.65f,-2.5f);m.Owner=target.Id;
                }
                var motion=target.Motion;motion.CatchPauseFrames=CombatPhysics.CatchPauseFrames;target.Motion=motion;
                break;
            }
            if(!dangerous||target.Invincible>0)continue;
            if(m.Kind==Equipment.EmberVessel&&!hazard){Blast(m,CombatPhysics.EmberRadius);break;}
            Damage(target,m.Owner,m.Velocity);
            if(m.Kind==Equipment.StormJavelin&&!m.Chained)
            {
                m.Chained=true;
                var other=State.Fighters.Where(p=>p.Alive&&p.Invincible==0&&p.Dodge==0&&p.Id!=target.Id&&p.Id!=m.Owner&&
                    (State.Mode!=BattleMode.Campaign||!p.IsPlayer||owner is not {IsPlayer:true}))
                    .Where(p=>ArenaLayout.Delta(m.Position,p.Center).LengthSquared()<=CombatPhysics.StormRadius*CombatPhysics.StormRadius&&
                        CombatPhysics.HasLineOfSight(Layout,m.Position,p.Center))
                    .MinBy(p=>ArenaLayout.Delta(m.Position,p.Center).LengthSquared());
                if(other!=null){Damage(other,m.Owner,m.Velocity);State.Events.Add(new("storm-chain",other.Center));}
            }
            if(hazard)m.Removed=true;
            else
            {
                m.Falling=true;m.OwnerProtected=false;m.Velocity=new(-Math.Sign(m.Velocity.X)*.65f,-2.5f);
                m.PixelRemainder=Vector2.Zero;
            }
            break;
        }
    }
    private bool Overlaps(Missile missile,Fighter target)
    {
        float width=target.IsPlayer?8:EnemyController.Profile(target.Enemy).Width;
        float height=target.IsPlayer?(target.Motion.SmallHitbox?6:14):EnemyController.Profile(target.Enemy).Height;
        var shape=MissileBounds(missile);var delta=ArenaLayout.Delta(missile.Position+shape.Offset,target.Position-new Vector2(0,height/2));
        return MathF.Abs(delta.X)<(width+shape.Width)/2&&MathF.Abs(delta.Y)<(height+shape.Height)/2;
    }
    private static (float Width,float Height,Vector2 Offset) MissileBounds(Missile m)
    {
        if(m.EnemyAttack==EnemyProjectileKind.GroundWave)return (7,5,new(.5f,.5f));
        if(m.EnemyAttack==EnemyProjectileKind.Venom)return (4,4,Vector2.Zero);
        var profile=CombatPhysics.Profile(m.Kind);
        return (profile.Width,profile.Height,new(0,profile.Height%2*.5f));
    }
    private void MoveMissileAxis(Missile m,bool horizontal)
    {
        float value=(horizontal?m.PixelRemainder.X:m.PixelRemainder.Y)+(horizontal?m.Velocity.X:m.Velocity.Y);
        int distance=(int)Math.Round(value,MidpointRounding.ToEven);
        m.PixelRemainder=horizontal?new(value-distance,m.PixelRemainder.Y):new(m.PixelRemainder.X,value-distance);
        var step=horizontal?new Vector2(Math.Sign(distance),0):new Vector2(0,Math.Sign(distance));
        for(int i=0;i<Math.Abs(distance);i++)
        {
            var next=ArenaLayout.Wrap(m.Position+step);
            var shape=MissileBounds(m);
            if(Layout.Blocked(next+shape.Offset+new Vector2(0,shape.Height/2),shape.Width,shape.Height))
            {
                if(m.EnemyAttack is EnemyProjectileKind.Venom or EnemyProjectileKind.GroundWave){m.Removed=true;break;}
                var contact=CombatPhysics.HitTerrain(m.Kind,m.Velocity,horizontal,++m.Bounces,m.Falling,m.Returning,m.Direction);
                m.Velocity=contact.Velocity;m.Falling=contact.Falling;m.Returning=contact.Returning;m.Resting=contact.Resting;
                m.PickupDelay=contact.PickupDelay;if(m.Falling||m.Resting)m.OwnerProtected=false;
                m.PixelRemainder=horizontal?new(0,m.PixelRemainder.Y):new(m.PixelRemainder.X,0);
                if(contact.Detonate)Blast(m,CombatPhysics.EmberRadius);
                State.Events.Add(new("weapon-impact",m.Position));break;
            }
            m.Position=next;
        }
    }
    private void Blast(Missile m,float radius)
    {
        m.Removed=true;State.Events.Add(new("ember-burst",m.Position));
        var owner=State.Fighters.Find(p=>p.Id==m.Owner);
        foreach(var target in State.Fighters.Where(p=>p.Alive).ToArray())
            if((target.Id==m.Owner||State.Mode!=BattleMode.Campaign||owner is not {IsPlayer:true}||!target.IsPlayer)&&
                ArenaLayout.Delta(m.Position,target.Center).LengthSquared()<=radius*radius&&CombatPhysics.HasLineOfSight(Layout,m.Position,target.Center))
                Damage(target,m.Owner,ArenaLayout.Delta(m.Position,target.Center));
        foreach(var hazard in State.Missiles.Where(p=>p.EnemyAttack is EnemyProjectileKind.Venom or EnemyProjectileKind.GroundWave))
            if(ArenaLayout.Delta(m.Position,hazard.Position).LengthSquared()<=radius*radius&&CombatPhysics.HasLineOfSight(Layout,m.Position,hazard.Position))hazard.Removed=true;
    }
    private void ResolveContacts()
    {
        var alive=State.Fighters.Where(p=>p.Alive).ToArray();
        foreach(var p in alive.Where(p=>p.IsPlayer))foreach(var target in alive)
        {
            if(!p.Alive||!target.Alive||p.Id==target.Id||State.Mode==BattleMode.Campaign&&target.IsPlayer)continue;
            float width=target.IsPlayer?8:EnemyController.Profile(target.Enemy).Width;
            float height=target.IsPlayer?(target.Motion.SmallHitbox?6:14):EnemyController.Profile(target.Enemy).Height;
            var d=ArenaLayout.Delta(p.Position,target.Position);
            if(MathF.Abs(d.X)>=(8+width)/2||d.Y>height||d.Y< -14)continue;
            if(p.Velocity.Y>0&&d.Y>height/2)
            {
                var impact=p.Velocity;
                Damage(target,p.Id,impact);
                if(!target.Alive)target.Velocity=impact;
                // Align through the same fractional movement accumulator: setting
                // an integer position directly loses rounding at half-pixel ties.
                float align=target.Position.Y-height-(p.Position.Y+p.PixelRemainder.Y);
                float amount=p.PixelRemainder.Y+align;int pixels=(int)Math.Round(amount,MidpointRounding.ToEven);
                p.Position=ArenaLayout.Wrap(p.Position+new Vector2(0,pixels));
                p.PixelRemainder=new(p.PixelRemainder.X,amount-pixels);p.Velocity=new(p.Velocity.X,-3.2f);
                var motion=p.Motion;motion.VariableJump=true;motion.JumpGrace=0;motion.JumpBuffer=0;motion.Velocity=p.Velocity;p.Motion=motion;
            }
            else if(!target.IsPlayer&&p.Dodge==0&&EnemyController.CanContactHurt(target))Damage(p,target.Id);
        }
    }
    public bool Damage(Fighter target,int owner=-1,Vector2 force=default)
    {
        if(!target.Alive||target.Invincible>0||target.Dodge>0)return false;
        if(target.Shield>0){target.Shield--;target.Invincible=30;if(target.ShieldReserve>0)target.ShieldRecharge=45;Emit("shield-break",target);return false;}
        target.Health--;target.Invincible=target.IsPlayer?90:12;
        if(target.Alive){if(!target.IsPlayer)EnemyController.OnHit(target,force);Emit("enemy-hit",target);return false;}
        Emit(target.IsPlayer?"death":"enemy-defeat",target);
        if(target.IsPlayer){target.Lives--;target.Respawn=75;target.Spirit=SpiritMotor.Create(target,State.Mode==BattleMode.Versus);}
        else if(target.Enemy is EnemyKind.Captain or EnemyKind.Sentinel)State.Supplies.Add(new(){Id=identity++,Kind=Equipment.Manna,Position=target.Center});
        return true;
    }
    public bool ApplySupply(Fighter p,Equipment kind)
    {
        if(!p.Alive||!p.IsPlayer)return false;
        if(kind<Equipment.Manna){if(p.Ammo.Count>=CombatPhysics.AmmoCapacity)return false;p.Ammo.Insert(0,kind);if(p.Ammo.Count<CombatPhysics.AmmoCapacity)p.Ammo.Insert(0,kind);}
        else if(kind==Equipment.Manna){if(p.Invincible==0)p.Invincible=60;int count=Math.Min(3,CombatPhysics.AmmoCapacity-p.Ammo.Count);for(int i=0;i<count;i++)p.Ammo.Add(BaseWeapon(p.Hero));}
        else if(kind==Equipment.CovenantSeal){if(p.Shield>0&&p.ShieldReserve>0)return false;p.Shield=1;p.ShieldReserve=1;p.ShieldRecharge=0;}
        else p.Sandals=600;
        State.Events.Add(new("relic-pickup",p.Center,p.Id,(int)kind));return true;
    }
    private void AddSupplies(int wave)
    {
        for(int i=0;i<heroes.Length;i++)
        {
            int rotation=(State.Chapter+wave+i)%3;var start=Layout.Spawns[i%Layout.Spawns.Length]-new Vector2(0,16);
            Add((Equipment)(4+rotation),-14);Add((Equipment)(7+(rotation+1)%3),14);
            void Add(Equipment kind,int side)=>State.Supplies.Add(new(){Id=identity++,Kind=kind,Position=start,Start=start,
                Destination=FindSupplySpace(start+new Vector2(side,-10)),Animated=true});
        }
    }
    private Vector2 FindSupplySpace(Vector2 preferred)
    {
        for(int rise=0;rise<=40;rise+=4)
        {
            var p=new Vector2(Math.Clamp(preferred.X,8,312),Math.Clamp(preferred.Y-rise,30,224));
            if(!Layout.Blocked(p+new Vector2(0,6),12,12))return p;
        }
        for(int y=32;y<=224;y+=8)for(int x=8;x<=312;x+=8)
            if(!Layout.Blocked(new(x,y+6),12,12))return new(x,y);
        throw new InvalidDataException("Arena has no space for supplies.");
    }
    private Vector2 FindEnemySpace(Vector2 feet,EnemyKind kind)
    {
        var profile=EnemyController.Profile(kind);
        for(int radius=0;radius<=40;radius+=2)for(int dy=-radius;dy<=radius;dy+=2)
            foreach(int sign in new[]{-1,1})
            {
                var point=ArenaLayout.Wrap(feet+new Vector2((radius-Math.Abs(dy))*sign,dy));
                if(!Layout.Blocked(point,profile.Width,profile.Height))return point;
            }
        throw new InvalidDataException("Arena portal has no clearance for "+kind);
    }
    private void AdvanceArrivals()
    {
        foreach(var arrival in State.Arrivals.ToArray())
        {
            if(--arrival.Delay>0)continue;
            var enemy=EnemyController.Spawn(identity++,arrival.Kind,FindEnemySpace(arrival.Position+new Vector2(0,7),arrival.Kind));
            enemy.Facing=arrival.Facing;State.Fighters.Add(enemy);State.Arrivals.Remove(arrival);Emit("enemy-spawn",enemy);
        }
    }
    private void AdvanceCampaign()
    {
        if(State.Fighters.Where(p=>p.IsPlayer).All(p=>!p.Alive&&p.Lives<=0)){State.Arrivals.Clear();State.Phase=BattlePhase.Defeat;return;}
        if(State.Phase is BattlePhase.Arrival or BattlePhase.Intermission)
        {
            if(--State.Delay>0)return;
            State.Wave++;State.Phase=BattlePhase.Fighting;clearDelay=0;
            var portals=Layout.Portals.Length>0?Layout.Portals:Layout.Spawns;
            int[] queued=new int[portals.Length];
            for(int i=0;i<waves[State.Wave].Length;i++)Queue(waves[State.Wave][i],(i+State.Wave)%portals.Length);
            if(heroes.Length==2)Queue(waves[State.Wave][0],(State.Wave+1)%portals.Length);
            void Queue(EnemyKind kind,int portal)
            {
                int count=portalCounts.GetValueOrDefault(portal);portalCounts[portal]=count+1;
                State.Arrivals.Add(new(){Kind=kind,Position=portals[portal],Delay=45+42*queued[portal]++,Facing=count%2==0?-1:1});
            }
        }
        else if(State.Phase==BattlePhase.Fighting)
        {
            if(State.Arrivals.Count>0||State.Fighters.Any(p=>!p.IsPlayer&&p.Alive)){clearDelay=0;return;}
            if(++clearDelay<20)return;
            State.CompletedWaves++;
            if(State.CompletedWaves==3){State.Phase=BattlePhase.Victory;State.Events.Add(new("checkpoint",Vector2.Zero));}
            else{State.Phase=BattlePhase.Intermission;State.Delay=110;AddSupplies(State.CompletedWaves);foreach(var p in State.Fighters.Where(p=>p.IsPlayer&&p.Alive))while(p.Ammo.Count<3)p.Ammo.Add(BaseWeapon(p.Hero));}
        }
    }
    private void AdvanceVersus()
    {
        if(State.Phase==BattlePhase.Arrival){if(--State.Delay<=0)State.Phase=BattlePhase.Fighting;return;}
        if(State.Phase!=BattlePhase.Fighting)return;
        if(--supplyTimer==0){AddSupplies(State.Round);supplyTimer=600;}
        var survivors=State.Fighters.Where(p=>p.IsPlayer&&p.Alive).ToArray();
        if(survivors.Length>1){State.Delay=90;ghostWait=90;return;}
        if(survivors.Length==1&&ghostWait>0&&State.Fighters.Any(p=>p.Spirit is {Playable:true,Phase:SpiritPhase.Waiting or SpiritPhase.Emerging or SpiritPhase.Drifting or SpiritPhase.Dashing}))
        {ghostWait--;return;}
        if(--State.Delay>0)return;
        State.Winner=survivors.Length==1?survivors[0].Slot:-1;
        if(State.Winner>=0)State.Scores[State.Winner]++;
        State.Phase=State.Scores.Any(s=>s>=5)?BattlePhase.MatchOver:BattlePhase.RoundOver;
    }
    private void Emit(string kind,Fighter p)=>State.Events.Add(new(kind,p.Center,p.Id));
    private static float Toward(float value,float target,float step)=>value<target?Math.Min(target,value+step):Math.Max(target,value-step);
}
