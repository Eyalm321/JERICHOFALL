using System.Numerics;
using System.Net.Sockets;
using System.Text.Json;
using Jerichofall;

if(args.Length==2&&args[0]=="--audit"){DependencyAudit.Run(args[1]);return;}

int checks=MovementParityChecks.Run()+InteractionParityChecks.Run()+CombatParityChecks.Run()+DeathSpiritChecks.Run();
void Check(bool condition,string reason){checks++;if(!condition)throw new Exception(reason);}
var root=Path.GetFullPath(args.FirstOrDefault()??"build/standalone/Content");
var data=new GameContent(root);
foreach(string id in CampaignCatalog.RouteIds.Concat(new[]{"jerusalem","carmel","negev"}))
{
    var arena=data.Arena(id);Check(arena.Spawns.Length>=4,"Arena spawns: "+id);
    foreach(var spawn in arena.Spawns)Check(!arena.Blocked(spawn),"Clear authored spawn: "+id);
}
ArenaLayout Flat()=>new(){Id="test",Title="TEST",Solids=Enumerable.Range(0,24).Select(y=>new string(y==23?'1':'0',32)).ToArray(),Background=Enumerable.Repeat(new string('0',32),24).ToArray(),Spawns=[new(40,230),new(120,230),new(200,230),new(280,230)],Portals=[new(160,100)]};
Battle Match()=>new(Flat(),[Hero.David,Hero.Moses],BattleMode.Versus);
var movement=Match();var player=movement.State.Fighters[0];
for(int i=0;i<100;i++)movement.Step([default,default]);
float origin=player.Position.Y;movement.Step([new(0,0,Buttons.Jump),default]);
Check(player.Position.Y<origin&&player.Velocity.Y<0,"Jump leaves ground");
for(int i=0;i<70;i++)movement.Step([default,default]);
Check(MathF.Abs(player.Position.Y-origin)<1,"Gravity returns to floor");
float x=player.Position.X;for(int i=0;i<20;i++)movement.Step([new(1),default]);Check(player.Position.X>x+15,"Run advances body");
movement.Step([new(1,0,Buttons.Dodge),default]);Check(player.Dodge>0&&player.DodgeCooldown==0,"Dodge starts; cooldown begins when it ends");
for(int i=0;i<100;i++)movement.Step([default,default]);
Check(player.Dodge==0&&player.DodgeCooldown==0,"Dodge completes");
long before=movement.State.Frame;movement.Step([new(0,0,Buttons.Pause),default]);
for(int i=0;i<20;i++)movement.Step([default,default]);Check(movement.State.Frame==before&&movement.State.Paused,"Pause freezes physics");
movement.Step([new(0,0,Buttons.Pause),default]);Check(!movement.State.Paused,"Pause resumes");
var wrap=ArenaLayout.Wrap(new(-1,241));Check(wrap==new Vector2(319,1),"Both arena seams wrap");

foreach(Equipment weapon in Enum.GetValues<Equipment>().Where(e=>e<Equipment.Manna))
{
    var game=Match();var a=game.State.Fighters[0];var b=game.State.Fighters[1];
    a.Position=new(100,200);b.Position=new(124,200);a.Invincible=b.Invincible=0;
    game.Launch(a,weapon,0);for(int frame=0;frame<30&&b.Alive;frame++)game.Step([default,default]);
    Check(!b.Alive,"Weapon hits another fighter: "+weapon);
}
var catchGame=Match();var catcher=catchGame.State.Fighters[1];catcher.Invincible=0;catcher.Dodge=7;
catchGame.State.Missiles.Add(new(){Id=50,Owner=0,Position=catcher.Center-new Vector2(4,0),Velocity=new(1,0),Kind=Equipment.Stone,Age=20});
int ammo=catcher.Ammo.Count;catchGame.Step([default,default]);Check(catcher.Alive&&catcher.Ammo.Count==ammo+1,"Dodge catches ammunition");
var equipment=Match();var equipped=equipment.State.Fighters[0];
foreach(var kind in new[]{Equipment.Manna,Equipment.CovenantSeal,Equipment.WindSandals})Check(equipment.ApplySupply(equipped,kind),"Collect "+kind);
Check(equipped.Shield==1&&equipped.Sandals>0,"Aid effects applied");equipped.Invincible=0;
Check(!equipment.Damage(equipped)&&equipped.Alive&&equipped.Shield==0,"Seal absorbs hit");
for(int i=0;i<480;i++)equipment.Step([default,default]);Check(equipped.Shield==1,"Seal recharge");

int waves=0,enemies=0;
for(int chapter=0;chapter<18;chapter++)
{
    var game=data.CampaignBattle(chapter,chapter%2==0?[Hero.David]:[Hero.Moses,Hero.Joshua]);
    for(int frame=0;frame<2000&&game.State.Phase!=BattlePhase.Victory;frame++)
    {
        foreach(var p in game.State.Fighters.Where(p=>p.IsPlayer))p.Invincible=100;
        game.Step([default,default]);
        foreach(var e in game.State.Fighters.Where(p=>!p.IsPlayer&&p.Alive).ToArray())
        {
            if(e.Invincible>0)continue;enemies++;
            while(e.Alive){e.Invincible=0;game.Damage(e,0);}
        }
    }
    Check(game.State.Phase==BattlePhase.Victory&&game.State.CompletedWaves==3,"All encounters complete: "+chapter);waves+=game.State.CompletedWaves;
}
var defeat=data.CampaignBattle(0,[Hero.Samson]);
for(int death=0;death<3;death++)
{
    var p=defeat.State.Fighters[0];p.Invincible=0;p.Shield=0;defeat.Damage(p);
    for(int i=0;i<81;i++)defeat.Step([default]);
}
Check(defeat.State.Phase==BattlePhase.Defeat,"Three lost lives produce defeat");
var scoring=Match();
for(int round=0;round<5;round++)
{
    for(int i=0;i<110;i++)scoring.Step([default,default]);var loser=scoring.State.Fighters[1];loser.Invincible=0;scoring.Damage(loser,0);
    for(int i=0;i<185;i++)scoring.Step([default,default]);Check(scoring.State.Scores[0]==round+1,"Versus awards survivor score after ghost opportunity");
    if(round<4)scoring.Step([new(0,0,Buttons.Confirm),default]);
}
Check(scoring.State.Phase==BattlePhase.MatchOver,"First to five ends match");
scoring.Step([new(0,0,Buttons.Confirm),default]);Check(scoring.State.Scores.All(x=>x==0),"Rematch resets scores");

string temp=Path.Combine(Path.GetTempPath(),"jerichofall-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try
{
    var store=new CampaignSave(Path.Combine(temp,"journey.json"),_=>{});var journey=new CampaignProgress();
    Check(!journey.IsUnlocked(1),"Later chapter initially locked");
    for(int i=0;i<18;i++){journey.Complete(i,i+100);Check(store.Write(journey),"Save clear");journey=store.Load();Check(journey.Completed[i],"Reload clear");}
    Check(journey.Finished,"Finished journey persists");Check(store.Archive(),"Completed save archives");
    File.WriteAllText(store.Path,"broken");Check(store.Load().CompletedCount>=17,"Corrupt primary recovers rolling backup");
    Check(Directory.GetFiles(temp,"*.bak").Length>=2,"Recovery preserves corrupt data and previous journey");
}
finally{Directory.Delete(temp,true);}

using(var host=new OnlineHost(data.Fingerprint,"jerusalem",4,Hero.David,0,true))
using(var c1=new OnlineClient(data.Fingerprint,"127.0.0.1",host.Port,Hero.Moses))
using(var c2=new OnlineClient(data.Fingerprint,"127.0.0.1",host.Port,Hero.Samson))
using(var c3=new OnlineClient(data.Fingerprint,"127.0.0.1",host.Port,Hero.Joshua))
{
    OnlineClient[] clients=[c1,c2,c3];
    for(int i=0;i<500&&!host.Ready;i++){host.Heartbeat();foreach(var c in clients)c.Send(default);await Task.Delay(10);}
    Check(host.Ready,"Four-player TCP lobby");
    var game=new Battle(data.Arena("jerusalem"),host.StartRoster(),BattleMode.Versus);
    Check(game.State.Fighters.Select(p=>p.Hero).Distinct().Count()==4,"Network roster retains all four heroes");
    for(int frame=0;frame<600;frame++)
    {
        if(frame==100){var p=game.State.Fighters[1];p.Invincible=0;p.Shield=0;game.Damage(p);}
        foreach(var c in clients)c.Send(new(frame%120<60?1:-1,0,frame%47<10?Buttons.Jump:Buttons.None));
        await Task.Delay(2);game.Step(host.Inputs(new(1)));host.Broadcast(game.State);
    }
    for(int i=0;i<300&&clients.Any(c=>c.State?.Frame!=game.State.Frame||c.State?.EffectFrame!=game.State.EffectFrame);i++){foreach(var c in clients)c.Send(default);host.Broadcast(game.State);await Task.Delay(5);}
    foreach(var c in clients)
    {
        Check(c.Error==null&&c.State?.Frame==game.State.Frame&&c.State?.EffectFrame==game.State.EffectFrame,"Client receives final simulation and effect frames");
        Check(JsonSerializer.Serialize(c.State,GameJson.Options)==JsonSerializer.Serialize(game.State,GameJson.Options),"Client snapshot matches host exactly");
        Check(c.State!.Fighters.Any(p=>p.Spirit!=null),"Persistent death spirit survives snapshots without the death event");
    }
    c3.Dispose();for(int i=0;i<100&&host.Error==null;i++)await Task.Delay(10);Check(host.Error!=null,"Disconnect reported to host");
}
using(var host=new OnlineHost(data.Fingerprint,"jerusalem",2,Hero.David,0,true))
using(var mismatch=new OnlineClient(new string('0',64),"127.0.0.1",host.Port,Hero.Moses))
{
    for(int i=0;i<300&&mismatch.Error==null;i++)await Task.Delay(10);
    Check(mismatch.Error?.Contains("mismatch")==true&&!host.Ready,"Reject mismatched build/content");
}
// Exercise the decoder across an actual socket, including JSON nulls which can
// bypass C# nullable annotations. Invalid hosts must produce an actionable error.
foreach(string fault in new[]{"negative-frame","null-missiles","null-fighter","null-arena","invalid-spirit","extreme-spirit"})
{
    using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
    using var client=new OnlineClient(data.Fingerprint,"127.0.0.1",((System.Net.IPEndPoint)listener.LocalEndpoint).Port,Hero.Moses);
    using var peer=await listener.AcceptTcpClientAsync();var stream=peer.GetStream();
    async Task Send(WireMessage message)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(message,GameJson.Options);var header=new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header,bytes.Length);await stream.WriteAsync(header);await stream.WriteAsync(bytes);
    }
    await Send(new(){Type="welcome",Fingerprint=data.Fingerprint,Slot=1,Players=2,Arena=fault=="null-arena"?null!:"jerusalem"});
    if(fault!="null-arena")
    {
        var state=Match().State;
        if(fault=="negative-frame")state.Frame=-1;
        if(fault=="null-missiles")state.Missiles=null!;
        if(fault=="null-fighter")state.Fighters[0]=null!;
        if(fault=="invalid-spirit")state.Fighters[0].Spirit=new(){Phase=(SpiritPhase)999};
        if(fault=="extreme-spirit")state.Fighters[0].Spirit=new(){Velocity=new(1e20f,0)};
        await Send(new(){Type="state",State=state});
    }
    for(int i=0;i<300&&client.Error==null;i++)await Task.Delay(10);
    Check(client.Error!=null&&client.State==null,"Reject invalid host data: "+fault);
}
var report=new{passed=true,checks,arenas=21,chapters=18,waves,enemies,networkPlayers=4,engine="Jerichofall.Core",unexpectedAssemblyLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a!=typeof(DependencyAudit).Assembly&&!DependencyAudit.IsAllowedAssembly(a.GetName().Name!)),limitations="Campaign completion uses controlled enemy damage; networking uses local TCP. Human balance, controllers and Internet routes need playtesting."};
Check(!report.unexpectedAssemblyLoaded,"Only allowed assemblies loaded");
Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
if(args.Length>1)File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
