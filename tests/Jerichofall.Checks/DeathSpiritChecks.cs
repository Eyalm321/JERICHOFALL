using System.Numerics;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Jerichofall;

static class DeathSpiritChecks
{
    public static int Run([CallerFilePath]string path="")
    {
        int checks=0;
        void Check(bool condition,string reason){checks++;if(!condition)throw new Exception("Death spirit: "+reason);}
        ArenaLayout Flat()=>new(){Id="spirit-test",Solids=Enumerable.Range(0,24).Select(y=>new string(y>=22?'1':'0',32)).ToArray(),Background=Enumerable.Repeat(new string('0',32),24).ToArray(),Spawns=[new(80,220),new(240,220),new(180,220),new(300,220)],Portals=[new(160,80)]};
        var layout=Flat();var spirit=SpiritMotor.Create(new(){Position=new(80,220)},true);
        using(var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"Fixtures/spirit-regression.json"))))
        {
            Buttons previous=0;
            foreach(var sample in fixture.RootElement.GetProperty("trace").EnumerateArray())
            {
                var input=sample.GetProperty("input");var buttons=input.GetProperty("dodge").GetBoolean()?Buttons.Dodge:0;
                SpiritMotor.Step(spirit,layout,new(input.GetProperty("x").GetInt32(),input.GetProperty("y").GetInt32(),buttons),previous);previous=buttons;
                int frame=sample.GetProperty("frame").GetInt32();var ghost=sample.GetProperty("ghost");
                if(ghost.ValueKind==JsonValueKind.Null){Check(!spirit.Visible,$"frame {frame}: wait before emergence");continue;}
                void Near(float actual,string field,float tolerance=.002f)=>Check(Math.Abs(actual-ghost.GetProperty(field).GetSingle())<=tolerance,$"frame {frame}: {field} got {actual}, observed {ghost.GetProperty(field)}");
                Check((int)spirit.Phase-1==ghost.GetProperty("State").GetInt32(),$"frame {frame}: observed phase");
                Near(spirit.Velocity.X,"vx");Near(spirit.Velocity.Y,"vy");Near(spirit.CruiseSpeed,"moveMax");
                Near(spirit.Cooldown,"cooldown");Near(spirit.Warmup,"killDelay");
                Near(spirit.Position.X,"X");Near(spirit.Position.Y,"Y");
            }
        }
        Battle Match(int players=2)=>new(layout,Enumerable.Range(0,players).Select(i=>(Hero)i).ToArray(),BattleMode.Versus);
        void Kill(Battle b,int slot){var p=b.State.Fighters[slot];p.Invincible=0;p.Shield=0;Check(b.Damage(p),"lethal hit");}
        var game=Match(3);game.State.Phase=BattlePhase.Fighting;Kill(game,0);
        Check(game.State.Fighters[0].Spirit is {Phase:SpiritPhase.Waiting},"death persists in state before rendering");
        game.Step([new(0,0,Buttons.Pause)]);int age=game.State.Fighters[0].Spirit!.Age;
        for(int i=0;i<10;i++)game.Step([]);
        Check(game.State.Fighters[0].Spirit!.Age==age,"pause freezes the spirit");game.Step([new(0,0,Buttons.Pause)]);
        for(int i=0;i<125;i++)game.Step([]);
        var ghostState=game.State.Fighters[0].Spirit!;Check(ghostState.Tangible&&ghostState.Warmup==0,"ghost becomes interactive after emergence and grace period");
        var survivor=game.State.Fighters[1];survivor.Invincible=0;survivor.Position=ghostState.Position+new Vector2(0,8);survivor.Velocity=Vector2.Zero;
        game.Step([]);Check(!survivor.Alive,"ghost touches and eliminates a survivor");
        var shooter=game.State.Fighters[2];shooter.Position=new(240,220);
        game.State.Missiles.Add(new(){Id=999,Owner=shooter.Id,Kind=Equipment.Stone,Position=ghostState.Position-new Vector2(6,0),Velocity=new(6,0),Direction=0});
        game.Step([]);Check(ghostState.Phase==SpiritPhase.Dispersing&&ghostState.Banished,"projectile banishes a ghost");
        var stomp=Match(3);stomp.State.Phase=BattlePhase.Fighting;Kill(stomp,0);
        var shade=stomp.State.Fighters[0].Spirit!;shade.Phase=SpiritPhase.Drifting;shade.Position=new(150,170);shade.Velocity=Vector2.Zero;shade.Warmup=0;
        var jumper=stomp.State.Fighters[1];jumper.Position=new(150,166);jumper.Velocity=new(0,2);jumper.Invincible=0;
        var motor=jumper.Motion;motor.Velocity=jumper.Velocity;jumper.Motion=motor;stomp.Step([]);
        Check(shade.Banished&&jumper.Alive&&jumper.Velocity.Y<0,"stomp banishes and bounces");
        var ending=Match();ending.State.Phase=BattlePhase.Fighting;ending.State.Delay=90;Kill(ending,0);
        for(int i=0;i<100;i++)ending.Step([]);Check(ending.State.Phase==BattlePhase.Fighting,"round waits for a revenge opportunity");
        for(int i=0;i<90;i++)ending.Step([]);Check(ending.State.Phase==BattlePhase.RoundOver&&ending.State.Scores[1]==1,"round ends after bounded ghost wait");
        long frozen=ending.State.Frame;for(int i=0;i<40;i++)ending.Step([]);
        Check(ending.State.Frame==frozen&&ending.State.Fighters[0].Spirit!.Phase==SpiritPhase.Gone,"spirit finishes fading after round results");
        ending.Step([new(0,0,Buttons.Confirm)]);Check(ending.State.Fighters.All(p=>p.Spirit==null),"new round removes old ghosts");
        var campaign=new Battle(layout,[Hero.David],BattleMode.Campaign,0,[[EnemyKind.Guard],[EnemyKind.Guard],[EnemyKind.Guard]]);
        Kill(campaign,0);var soul=campaign.State.Fighters[0].Spirit!;
        Check(soul.Visible&&!soul.Playable,"campaign death shows a harmless spirit immediately");
        for(int i=0;i<75;i++)campaign.Step([]);
        Check(campaign.State.Fighters[0].Alive&&soul.Phase==SpiritPhase.Dispersing,"campaign respawn disperses the spirit");
        for(int i=0;i<35;i++)campaign.Step([]);Check(soul.Phase==SpiritPhase.Gone,"no duplicate spirit after respawn");
        var p=campaign.State.Fighters[0];p.Lives=1;Kill(campaign,0);campaign.Step([]);
        for(int i=0;i<140;i++)campaign.Step([]);
        Check(campaign.State.Phase==BattlePhase.Defeat&&p.Spirit!.Phase==SpiritPhase.Gone,"final death animates through defeat overlay");
        var wrapped=SpiritMotor.Create(new(){Position=new(319,80)},true);wrapped.Phase=SpiritPhase.Drifting;wrapped.Position=new(319,80);wrapped.Velocity=new(2,0);
        SpiritMotor.Step(wrapped,layout,new(1),0);Check(wrapped.Position.X<5,"ghost crosses arena seam");
        Console.WriteLine($"Death spirits: {checks} checks, 240 measured frames plus death/respawn/combat/pause scenarios");return checks;
    }
}
