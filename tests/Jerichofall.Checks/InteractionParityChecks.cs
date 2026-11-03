using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jerichofall;

internal static class InteractionParityChecks
{
    public static int Run([CallerFilePath] string callerPath="")
    {
        string path=Path.Combine(Path.GetDirectoryName(callerPath)!,"Fixtures","combat-regression.json");
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        var differences=new List<object>();int checks=0;
        foreach(var scenario in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name=scenario.GetProperty("name").GetString()!;
            var samples=scenario.GetProperty("samples").EnumerateArray().ToArray();var initial=samples[0];
            var arena=new ArenaLayout{Id="interactions",Title="RECORDED INTERACTIONS",
                Solids=Enumerable.Range(0,24).Select(y=>new string(Enumerable.Range(0,32).Select(x=>y>=22||x==26&&y>=10?'1':'0').ToArray())).ToArray(),
                Background=Enumerable.Repeat(new string('0',32),24).ToArray(),Spawns=[new(80,220),new(120,220),new(160,220),new(200,220)]};
            var battle=new Battle(arena,[Hero.David,Hero.Moses],BattleMode.Versus);
            battle.State.Supplies.Clear();battle.State.Phase=BattlePhase.Fighting;
            var hero=battle.State.Fighters[0];var target=battle.State.Fighters[1];
            hero.Position=Vector(initial,"position");hero.Velocity=Vector(initial,"velocity");hero.PixelRemainder=Vector(initial,"remainder");hero.Invincible=0;
            hero.Motion=new(){JumpGrace=Number(initial,"grace"),JumpBuffer=Number(initial,"buffer")};
            bool hasTarget=initial.GetProperty("target").ValueKind!=JsonValueKind.Null;
            target.Position=hasTarget?new(Number(initial.GetProperty("target"),"x"),Number(initial.GetProperty("target"),"y")+8):new(20,220);
            target.Invincible=hasTarget?0:10000;
            foreach(var sample in samples.Skip(1))
            {
                var input=new PlayerInput(sample.GetProperty("x").GetInt32(),sample.GetProperty("y").GetInt32(),(Buttons)sample.GetProperty("buttons").GetInt32());
                // Held-jump stomp fixture has JumpCheck=true without a rising edge.
                if(input.Has(Buttons.Jump)&&!sample.GetProperty("jumpPressed").GetBoolean())hero.Previous|=Buttons.Jump;
                battle.Step([input,default]);int frame=sample.GetProperty("frame").GetInt32();
                void Compare(string field,float expected,float actual,float tolerance=.0002f)
                {
                    checks++;if(!float.IsFinite(actual)||MathF.Abs(expected-actual)>tolerance)
                        differences.Add(new{name,frame,field,expected,actual});
                }
                void Vec(string field,Vector2 expected,Vector2 actual)
                {Compare(field+"X",expected.X,actual.X);Compare(field+"Y",expected.Y,actual.Y);}
                Vec("position",Vector(sample,"position"),hero.Position);Vec("remainder",Vector(sample,"remainder"),hero.PixelRemainder);
                Vec("velocity",Vector(sample,"velocity"),hero.Velocity);
                Compare("ammo",Number(sample,"ammo"),hero.Ammo.Count);Compare("aiming",sample.GetProperty("aiming").GetBoolean()?1:0,hero.Aiming?1:0);
                if(hasTarget)Compare("targetDead",sample.GetProperty("target").GetProperty("dead").GetBoolean()?1:0,target.Alive?0:1);
                var expected=sample.GetProperty("arrows").EnumerateArray().ToArray();
                var actual=battle.State.Missiles.Where(m=>m.Age>0).OrderBy(m=>m.Id).ToArray();
                Compare("projectiles",expected.Length,actual.Length);
                for(int i=0;i<Math.Min(expected.Length,actual.Length);i++)
                {
                    var e=expected[i];var a=actual[i];
                    Vec("shotPosition",new(Number(e,"x"),Number(e,"y")),a.Position);
                    Vec("shotVelocity",new(Number(e,"vx"),Number(e,"vy")),a.Velocity);
                    string state=a.Resting?"LayingOnGround":a.Falling?"Falling":"Shooting";
                    Compare("shotState",e.GetProperty("state").GetString()==state?1:0,1);
                }
            }
        }
        string? report=Environment.GetEnvironmentVariable("JERICHOFALL_INTERACTION_REPORT");
        if(report!=null)File.WriteAllText(report,JsonSerializer.Serialize(new{checks,differences},new JsonSerializerOptions{WriteIndented=true}));
        if(differences.Count>0)throw new Exception("Recorded interaction differences: "+differences.Count+"\n"+JsonSerializer.Serialize(differences.Take(12)));
        Console.WriteLine($"PASS reference interactions: {checks} comparisons across six firing/stomp scenarios.");return checks;
    }
    private static float Number(JsonElement e,string property)=>e.GetProperty(property).GetSingle();
    private static Vector2 Vector(JsonElement e,string property)=>new(e.GetProperty(property)[0].GetSingle(),e.GetProperty(property)[1].GetSingle());
}
