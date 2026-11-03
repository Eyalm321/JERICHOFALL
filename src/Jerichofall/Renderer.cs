using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using V=Microsoft.Xna.Framework.Vector2;

namespace Jerichofall.View;

internal sealed class Renderer : IDisposable
{
    private readonly string root;
    private readonly Dictionary<string,Texture2D> textures=[];
    private readonly Texture2D[] bodies=new Texture2D[4],heads=new Texture2D[4],heldWeapons=new Texture2D[4],corpses=new Texture2D[4];
    private readonly SpriteFont font;
    private readonly CheckpointIcons icons;
    private readonly MapPresentation map;
    private readonly Dictionary<string,ArenaPresentation> arenaPresentation=[];
    private readonly List<Burst> bursts=[];
    private long lastEffectFrame=-1;
    private string effectArena="";
    private V mapCamera;
    private int lastMapAge=-1;
    private static readonly V[] WrapOffsets=[V.Zero,new(-320,0),new(320,0),new(0,-240),new(0,240),new(-320,-240),new(-320,240),new(320,-240),new(320,240)];
    private sealed record MapLabel(string Text,int X,int Y,string Tone);
    private sealed class MapPresentation
    {
        public int Width {get;set;}=480;
        public int Height {get;set;}=360;
        public MapLabel[] Labels {get;set;}=[];
        public Dictionary<string,int[][]> Routes {get;set;}=[];
    }
    private sealed class ArenaPresentation
    {
        public System.Numerics.Vector2[] Lamps {get;set;}=[];
        public Dictionary<string,int[]> BackgroundTiles {get;set;}=[];
    }
    private sealed record Burst(string Kind,V Position,long Frame,Color Tint);
    public static readonly Color Ink=new(8,17,31),Blue=new(22,49,76),Edge=new(57,110,144),Gold=new(231,194,115),Pale=new(228,236,218),Muted=new(139,172,187);
    private static readonly Color[] Garments=[new(49,124,182),new(66,127,163),new(52,90,155),new(36,77,152)];
    public Renderer(GraphicsDevice device,SpriteBatch batch,ContentManager content,string root)
    {
        this.root=root;Canvas2D.Device=device;Canvas2D.SpriteBatch=batch;Canvas2D.Pixel=Canvas2D.Texture(1,1,[Color.White]);
        font=content.Load<SpriteFont>("fonts/covenant-compact");icons=new(device);CampaignEnemyArt.Initialize(root);
        map=GameJson.Read<MapPresentation>(Path.Combine(root,"map.json"));
        for(int i=0;i<4;i++)
        {
            string name=((Hero)i).ToString().ToLowerInvariant();
            bodies[i]=Canvas2D.Texture(144,20,BiblicalSprites.Render("body",name,144,20,12,20,Garments[i],Pale));
            heads[i]=Canvas2D.Texture(50,10,BiblicalSprites.Render("headNormal",name,50,10,10,10,Garments[i],Pale));
            heldWeapons[i]=Canvas2D.Texture(60,10,BiblicalSprites.Render("bow",name,60,10,12,10,Garments[i],Pale));
            corpses[i]=Canvas2D.Texture(72,20,BiblicalSprites.Render("corpse",name,72,20,12,20,Garments[i],Pale));
        }
        textures["@sky"]=Canvas2D.Texture(320,240,BrandingArt.Sky(320,240));
        textures["@logo"]=Canvas2D.Texture(320,80,BrandingArt.Logo(320,80));
    }
    public Texture2D Image(string name)
    {
        if(textures.TryGetValue(name,out var texture))return texture;
        using var file=File.OpenRead(Path.Combine(root,name));return textures[name]=Texture2D.FromStream(Canvas2D.Device,file);
    }
    public void Text(string text,float x,float y,Color? tint=null,bool center=true,float scale=1)
    {
        text=new string(text.ToUpperInvariant().Select(c=>c is >= ' ' and <= '~'?c:'?').ToArray());
        var position=new V(MathF.Round(x),MathF.Round(y));if(center)position.X-=(int)(font.MeasureString(text).X*scale/2);
        foreach(var offset in new[]{new V(-1,0),new V(1,0),new V(0,-1),new V(0,1)})
            Canvas2D.SpriteBatch.DrawString(font,text,position+offset,Color.Black,0,V.Zero,scale,SpriteEffects.None,0);
        Canvas2D.SpriteBatch.DrawString(font,text,position,tint??Pale,0,V.Zero,scale,SpriteEffects.None,0);
    }
    public void Panel(int x,int y,int width,int height)
    {
        Canvas2D.Rect(x,y,width,height,Edge);Canvas2D.Rect(x+1,y+1,width-2,height-2,Ink);Canvas2D.Rect(x+2,y+2,width-4,height-4,Blue);Canvas2D.Rect(x+3,y+3,width-6,height-6,Ink);
    }
    public void Sky(){Canvas2D.SpriteBatch.Draw(Image("@sky"),V.Zero,Color.White);}
    public void Title(int selected)
    {
        Sky();Canvas2D.SpriteBatch.Draw(Image("@logo"),new V(0,8),Color.White);Text("EXODUS TO CANAAN",160,70,Gold);
        Panel(60,92,200,121);
        string[] items=["EXODUS CAMPAIGN","LOCAL VERSUS","HOST ONLINE VERSUS","JOIN ONLINE VERSUS","CONTROLS"];
        for(int i=0;i<items.Length;i++)Text((i==selected?"> ":"")+items[i],160,104+i*21,i==selected?Gold:Pale);
        Text("ENTER SELECT   F11 FULLSCREEN   M MUSIC",160,226,Muted);
    }
    public void Heroes(int hero,int second,int count,bool campaign,string arena)
    {
        Sky();Text(campaign?"CHOOSE YOUR TRAVELERS":"CHOOSE YOUR CHAMPIONS",160,13,Gold);
        string[] names=["david","moses","samson","joshua"];
        for(int i=0;i<4;i++)
        {
            int x=5+i*79;Panel(x,35,73,150);
            if(i==hero)Canvas2D.Rect(x+2,37,69,146,new Color(39,79,110));
            Canvas2D.SpriteBatch.Draw(Image("portraits/"+names[i]+".png"),new Rectangle(x+3,42,67,127),Color.White);
            Text(names[i],x+36,173,i==hero?Gold:Pale);
            if(i==hero)Text("P1",x+36,26,Gold);
            if(count>1&&i==second)Text("P2",x+36,188,Gold);
        }
        Text(campaign?$"{count} LOCAL HERO{(count==1?"":"ES")}":$"{count} PLAYERS / {arena}",160,202,Gold);
        Text("LEFT/RIGHT HERO   Q/E P2   TAB PLAYERS",160,215,Muted);
        Text(campaign?"ENTER BEGIN   ESC BACK":"F7 ARENA   ENTER BEGIN   ESC BACK",160,229,Muted);
    }
    public void Map(CampaignCatalog catalog,CampaignProgress progress,int selected,int age)
    {
        var image=Image("overworld.png");var current=catalog.Chapters[selected];
        var target=new V(Math.Clamp(current.X-160,0,map.Width-320),Math.Clamp(current.Y-79,0,map.Height-159));
        mapCamera=lastMapAge<0||age-lastMapAge>5?target:V.Lerp(mapCamera,target,.2f);lastMapAge=age;
        var offset=new V(-MathF.Round(mapCamera.X),25-MathF.Round(mapCamera.Y));
        Canvas2D.SpriteBatch.Draw(image,new Rectangle((int)offset.X,(int)offset.Y,map.Width,map.Height),Color.White);
        V Project(int x,int y)=>new V(x,y)+offset;
        foreach(var label in map.Labels)
        {
            var point=Project(label.X,label.Y);if(point.Y<30||point.Y>177)continue;
            Text(label.Text,point.X,point.Y,label.Tone=="sea"?Muted:label.Tone=="green"?Pale:Gold);
        }
        for(int i=0;i<catalog.Chapters.Length-1;i++)
        {
            var a=catalog.Chapters[i];var b=catalog.Chapters[i+1];var previous=Project(a.X,a.Y);
            var tint=progress.Completed[i]?Gold:new Color(79,67,66);
            foreach(var waypoint in map.Routes.GetValueOrDefault(a.Id)??[]){var next=Project(waypoint[0],waypoint[1]);Dotted(previous,next,tint);previous=next;}
            Dotted(previous,Project(b.X,b.Y),tint);
        }
        for(int i=0;i<catalog.Chapters.Length;i++)
        {
            var chapter=catalog.Chapters[i];var point=Project(chapter.X,chapter.Y);
            if(point.X< -18||point.X>338||point.Y<9||point.Y>200)continue;
            icons.Draw(chapter.Id,(int)point.X,(int)point.Y,progress.IsUnlocked(i),progress.Completed[i],selected==i,age);
        }
        Panel(0,0,320,25);Text("EXODUS",49,7,Gold,scale:1.5f);Text("EGYPT TO CANAAN",196,5);Text($"{progress.CompletedCount}/18 CHECKPOINTS CLEARED",196,15,Muted);
        Panel(0,184,320,56);icons.DrawPreview(current.Id,23,207,progress.IsUnlocked(selected));
        Text($"{selected+1:00} {current.Title}",171,190,Gold);Text(current.Subtitle,171,201);
        Text(progress.Completed[selected]?"CHECKPOINT SAVED - REPLAY CHAPTER":progress.IsUnlocked(selected)?"NEXT CHECKPOINT - 3 ENCOUNTERS":"LOCKED - FOLLOW THE JOURNEY",171,212,Muted);
        Text("ARROWS ROUTE   H HEROES   N NEW JOURNEY",160,225);Text("ENTER BRIEFING   ESC MENU",160,234,Muted);
    }
    private static void Dotted(V from,V to,Color tint)
    {
        var span=to-from;float length=span.Length();if(length<1)return;
        for(float distance=2;distance<length;distance+=5){var p=from+span*(distance/length);Canvas2D.Rect(MathF.Round(p.X),MathF.Round(p.Y),2,2,tint);}
    }
    public void Briefing(CampaignChapter chapter,int index)
    {
        Canvas2D.SpriteBatch.Draw(Image("arenas/"+chapter.Id+"/background.png"),V.Zero,Color.White);Canvas2D.Rect(0,0,320,240,Ink*.35f);
        Panel(14,48,292,145);icons.DrawPreview(chapter.Id,160,67,true);Text($"{index+1:00} {chapter.Title}",160,91,Gold);Text(chapter.Subtitle,160,107,Pale);
        Text(chapter.Intro[0],160,132,Muted);Text(chapter.Intro[1],160,145,Muted);Text("ENTER DEPART   ESC MAP",160,174,Gold);
    }
    public void Battle(ArenaLayout layout,BattleState state)
    {
        UpdateEffects(state);
        if(!arenaPresentation.TryGetValue(layout.Id,out var art))arenaPresentation[layout.Id]=art=GameJson.Read<ArenaPresentation>(Path.Combine(root,"arenas",layout.Id,"presentation.json"));
        Canvas2D.SpriteBatch.Draw(Image("arenas/"+layout.Id+"/background.png"),V.Zero,Color.White);
        Tiles(layout,layout.Background,Image("arenas/"+layout.Id+"/tiles-background.png"),Color.White,art.BackgroundTiles);
        foreach(var lamp in art.Lamps)
        {
            var point=Point(lamp);int phase=(int)(state.Frame/7+(long)lamp.X)%2;
            Canvas2D.SpriteBatch.Draw(CombatArt.Lamp(phase),point-new V(5,0),Color.White);
            if(state.Frame%7<3)Canvas2D.Rect(point.X+(state.Frame/7%3)-1,point.Y-3-(state.Frame/7%4),1,1,Gold*.75f);
        }
        Tiles(layout,layout.Solids,Image("arenas/"+layout.Id+"/foreground.png"),Color.White);
        foreach(var supply in state.Supplies.Where(s=>!s.Removed&&(s.Age<=840||s.Age/5%2!=0)))
        {
            var t=CombatArt.Weapon(supply.Kind);var point=Point(supply.Position);
            foreach(var offset in WrapOffsets)
            {
                var p=point+offset;if(p.X< -20||p.X>340||p.Y< -20||p.Y>260)continue;
                Canvas2D.Rect(p.X-5,p.Y+7,10,1,CombatArt.Tint(supply.Kind)*.6f);
                Canvas2D.SpriteBatch.Draw(t,p,null,Color.White,0,new V(t.Width/2f,t.Height/2f),1,SpriteEffects.None,0);
                if(state.Fighters.Any(f=>f.IsPlayer&&f.Alive&&ArenaLayout.Delta(f.Center,supply.Position).LengthSquared()<34*34))Text(CombatArt.Name(supply.Kind),p.X,p.Y-13,CombatArt.Tint(supply.Kind));
            }
        }
        foreach(var portal in state.Arrivals.Select(a=>a.Position).Distinct())
        {
            var p=Point(portal);Canvas2D.Rect(p.X-9,p.Y+5,18,4,Ink);Canvas2D.Rect(p.X-8,p.Y+6,16,2,Gold);
            for(int i=0;i<6;i++)
            {float angle=i*MathF.Tau/6+state.Frame*.035f;float radius=7+MathF.Sin(state.Frame*.09f)*2;Canvas2D.Rect(MathF.Round(p.X+MathF.Cos(angle)*radius),MathF.Round(p.Y-3+MathF.Sin(angle)*radius),2,2,Gold);}
            Canvas2D.Rect(p.X-1,p.Y+4-state.Frame*.7f%15,2,3,Pale);
        }
        DrawFallen(state);
        foreach(var missile in state.Missiles.Where(m=>!m.Removed&&m.BornFrame<state.Frame))DrawMissile(missile);
        foreach(var p in state.Fighters.Where(p=>p.Alive))
        {
            foreach(var offset in WrapOffsets)
            {
                var position=Point(p.Position)+offset;
                if(position.X< -20||position.X>340||position.Y< -20||position.Y>270)continue;
                if(p.IsPlayer)DrawHero(p,position,state.Frame);
                else DrawEnemy(layout,p,position,state.Frame);
            }
        }
        DrawSpirits(state);
        DrawEffects(state.EffectFrame);
        if(state.Mode==BattleMode.Campaign)
        {
            Canvas2D.Rect(0,0,320,24,Ink*.85f);Text($"{state.Chapter+1:00}  {layout.Title}",160,4,Gold);
            Text(state.Wave<0?"THREE ENCOUNTERS":$"ENCOUNTER {state.Wave+1}/3   FOES {state.Fighters.Count(p=>!p.IsPlayer&&p.Alive)+state.Arrivals.Count}",160,15);
            Text(string.Join("    ",state.Fighters.Where(p=>p.IsPlayer).Select(p=>$"P{p.Slot+1} LIVES {p.Lives}")),160,231,Gold);
        }
        else
        {
            int total=state.Fighters.Count(p=>p.IsPlayer);
            foreach(var p in state.Fighters.Where(p=>p.IsPlayer))
                Text($"P{p.Slot+1} {state.Scores[p.Slot]}",160+(p.Slot-(total-1)/2f)*38,231,p.Alive?Pale:Muted);
            if(state.Phase==BattlePhase.Arrival)Text($"{layout.Title} / ROUND {state.Round} / FIRST TO FIVE",160,21,Gold);
            if(state.Phase==BattlePhase.Fighting&&state.Fighters.Any(p=>p.Spirit?.Tangible==true))Text("SPIRITS: MOVE + FIRE OR DODGE TO DASH",160,6,Muted);
        }
        string? message=state.Phase switch{BattlePhase.Arrival=>"READY YOUR WEAPONS",BattlePhase.Intermission=>"ENCOUNTER CLEARED",BattlePhase.Victory=>"THE WAY IS OPEN",BattlePhase.Defeat=>"THE JOURNEY IS NOT OVER",BattlePhase.RoundOver=>state.Winner<0?"DRAW":$"PLAYER {state.Winner+1} WINS THE ROUND",BattlePhase.MatchOver=>$"PLAYER {state.Winner+1} WINS THE MATCH",_=>null};
        if(message!=null){Panel(24,92,272,48);Text(message,160,104,Gold);if(state.Phase is BattlePhase.Victory or BattlePhase.Defeat or BattlePhase.RoundOver or BattlePhase.MatchOver)Text("ENTER CONTINUE   BACKSPACE MENU",160,122,Muted);}
        if(state.Paused){Canvas2D.Rect(0,0,320,240,Ink*.65f);Panel(56,83,208,72);Text("JOURNEY PAUSED",160,97,Gold);Text("ESC RESUME   BACKSPACE MENU",160,121);Text("M MUSIC   F11 FULLSCREEN",160,138,Muted);}
    }
    private void DrawEnemy(ArenaLayout layout,Fighter p,V position,long frame)
    {
        var profile=EnemyController.Profile(p.Enemy);int height=profile.Height;
        if(p.Tell>0)
        {
            var tint=new Color(246,175,66);Canvas2D.Rect(position.X-1,position.Y-height-9,3,7,Ink);
            Canvas2D.Rect(position.X,position.Y-height-8,1,3,tint);Canvas2D.Rect(position.X,position.Y-height-4,1,1,tint);
            if(p.Enemy is EnemyKind.Cobra or EnemyKind.Raider)
                for(int d=8;d<48;d+=7){var delta=new System.Numerics.Vector2(MathF.Cos(p.Aim),MathF.Sin(p.Aim))*d;if(layout.Point(p.Center+delta))break;Canvas2D.Rect(position.X+delta.X,position.Y-8+delta.Y,1,1,tint);}
            else if(p.Enemy==EnemyKind.Sentinel){Canvas2D.Rect(position.X-24,position.Y-1,48,1,tint);Canvas2D.Rect(position.X-24,position.Y-3,1,2,tint);Canvas2D.Rect(position.X+23,position.Y-3,1,2,tint);}
            else for(int d=8;d<=24;d+=8)Canvas2D.Rect(position.X+p.Facing*d,position.Y-1,3,1,tint);
        }
        int pose=p.Brain.Phase is EnemyPhase.Attack or EnemyPhase.Windup?3:Math.Abs(p.Velocity.X)>.1f?1+p.Brain.Age/7%2:0;
        CampaignEnemyArt.Draw(p.Enemy,position,p.Facing,pose,p.Invincible>0&&p.Invincible%4<2,p.Tell>0?1-p.Tell/(float)profile.Windup:0);
        int max=profile.Health;
        if(max>1)for(int i=0;i<max;i++){float x=position.X-(max*4-1)/2f+i*4;Canvas2D.Rect(x-1,position.Y-height-6,4,4,Ink);Canvas2D.Rect(x,position.Y-height-5,2,2,i<p.Health?Gold:Muted*.4f);}
    }
    private void UpdateEffects(BattleState state)
    {
        if(state.EffectFrame<lastEffectFrame||state.Arena!=effectArena){bursts.Clear();lastEffectFrame=-1;effectArena=state.Arena;}
        if(lastEffectFrame==state.EffectFrame)return;
        lastEffectFrame=state.EffectFrame;bursts.RemoveAll(b=>state.EffectFrame-b.Frame>30);
        foreach(var e in state.Events)
        {
            if(e.Kind is "jump" or "land" or "weapon-impact" or "enemy-hit" or "enemy-defeat" or "death" or "shield-break" or "ember-burst" or "storm-chain" or "relic-pickup" or "enemy-spawn")
            {
                var tint=e.Kind switch{"ember-burst"=>new Color(240,137,58),"storm-chain" or "shield-break"=>new Color(137,223,243),"jump" or "land"=>new Color(211,189,145),_=>Gold};
                bursts.Add(new(e.Kind,Point(e.Position),state.EffectFrame,tint));
            }
        }
        if(bursts.Count>160)bursts.RemoveRange(0,bursts.Count-160);
    }
    private void DrawFallen(BattleState state)
    {
        foreach(var p in state.Fighters.Where(p=>p.IsPlayer&&p.Spirit is {Age:<102}))
        {
            var spirit=p.Spirit!;int age=spirit.Age,pose=age<12?4:0;
            var tint=Color.White*Math.Clamp((102-age)/40f,0,1);
            foreach(var offset in WrapOffsets)
            {
                var position=Point(spirit.Corpse)+offset;if(position.X< -20||position.X>340||position.Y< -20||position.Y>260)continue;
                Canvas2D.SpriteBatch.Draw(corpses[(int)p.Hero],position,new Rectangle(pose*12,0,12,20),tint,0,new V(6,18),1,spirit.CorpseFacing<0?SpriteEffects.FlipHorizontally:SpriteEffects.None,0);
            }
        }
    }
    private void DrawSpirits(BattleState state)
    {
        foreach(var p in state.Fighters.Where(p=>p.Spirit?.Visible==true))
        {
            var s=p.Spirit!;int[] idle=s.Happy>0?[9,10,11,10]:[6,7,8,7];
            int pose=s.Phase switch
            {
                SpiritPhase.Emerging=>Math.Min(5,(int)(s.PhaseAge/5.4f)),
                SpiritPhase.Dashing=>12+Math.Min(3,s.PhaseAge/3),
                SpiritPhase.Dispersing when s.Banished=>16+Math.Min(5,s.PhaseAge/5),
                SpiritPhase.Dispersing when s.PhaseAge>=15=>22+Math.Min(9,(s.PhaseAge-15)*2/3),
                _=>idle[(int)(s.Age/(s.Happy>0?7.8f:10.8f))%4]
            };
            float opacity=.9f+.1f*MathF.Sin(s.Age*MathF.Tau/120);
            if(s.Phase==SpiritPhase.Emerging)opacity*=Math.Min(1,(s.PhaseAge+5)/15f);
            if(s.Phase==SpiritPhase.Dispersing)opacity*=1-s.PhaseAge/(float)SpiritMotor.FadeFrames;
            var scale=s.Phase==SpiritPhase.Dashing?(Math.Abs(s.Velocity.X)<.5f?new V(.6f,1.4f):new V(1.4f,.6f)):V.One;
            var source=new Rectangle(pose%8*18,pose/8*25,18,25);
            foreach(var offset in WrapOffsets)
            {
                var position=Point(s.Position)+offset;if(position.X< -25||position.X>345||position.Y< -25||position.Y>265)continue;
                if(s.Phase==SpiritPhase.Dashing)
                    for(int i=3;i>0;i--)Canvas2D.SpriteBatch.Draw(Image("actors/spirit.png"),position-Point(s.Velocity)*i*.7f,source,Muted*(.16f/i),0,new V(9,12),scale,SpriteEffects.None,0);
                Canvas2D.SpriteBatch.Draw(Image("actors/spirit.png"),position,source,Color.White*opacity,0,new V(9,12),scale,s.Facing<0?SpriteEffects.FlipHorizontally:SpriteEffects.None,0);
                if(s.Phase!=SpiritPhase.Dispersing)
                {
                    Canvas2D.Rect(position.X-3,position.Y-13,6,1,Gold*opacity);
                    Canvas2D.Rect(position.X-4,position.Y-12,1,1,Gold*opacity);Canvas2D.Rect(position.X+3,position.Y-12,1,1,Gold*opacity);
                    Text($"P{p.Slot+1}",position.X,position.Y-23,Muted);
                }
            }
        }
    }
    private void DrawEffects(long frame)
    {
        foreach(var b in bursts)
        {
            float age=frame-b.Frame;bool large=b.Kind is "ember-burst" or "enemy-defeat" or "death" or "shield-break";
            float life=large?24:14;if(age>=life)continue;float fade=1-age/life;
            foreach(var offset in WrapOffsets)
            {
                var center=b.Position+offset;if(center.X< -32||center.X>352||center.Y< -32||center.Y>272)continue;
                if(b.Kind is "jump" or "land")
                {
                    center.Y+=8;
                    for(int i=0;i<6;i++){float sign=i%2==0?-1:1;Canvas2D.Rect(MathF.Round(center.X+sign*(2+i/2+age*.55f)),MathF.Round(center.Y-1-i/2-age*.09f),age<5?2:1,1,b.Tint*fade);}
                    continue;
                }
                int pieces=large?12:6;float radius=b.Kind=="ember-burst"?6+age:2+age*(large?.7f:.35f);
                for(int i=0;i<pieces;i++)
                {
                    float angle=i*MathF.Tau/pieces+(b.Frame%11)*.13f;var point=center+new V(MathF.Cos(angle),MathF.Sin(angle))*radius;
                    Canvas2D.Rect(MathF.Round(point.X),MathF.Round(point.Y),large&&age<8?2:1,large&&age<8?2:1,(i%3==0?Pale:b.Tint)*fade);
                }
                if(b.Kind=="weapon-impact"&&age<4){Canvas2D.Rect(center.X-2,center.Y,5,1,Pale*fade);Canvas2D.Rect(center.X,center.Y-2,1,5,Pale*fade);}
            }
        }
    }
    private void DrawHero(Fighter p,V position,long frame)
    {
        int pose=p.Motion.Mode==Motion.MovementMode.Dodge?(p.Motion.DodgeSlide?10+(int)(frame/4)%2:8)
            :p.Motion.SmallHitbox?9:p.Motion.Mode==Motion.MovementMode.Ledge||p.Motion.ClingDirection!=0?7
            :p.Velocity.Y<-.5f?4:p.Velocity.Y>.5f?6:Math.Abs(p.Velocity.X)>.25f?new[]{1,2,3,2}[(int)(frame/4.5f)%4]:0;
        var effect=p.Facing<0?SpriteEffects.FlipHorizontally:SpriteEffects.None;
        var tint=p.Invincible>0&&p.Invincible%6<3?Color.White*.5f:Color.White;
        if(p.Dodge>0)
            for(int i=3;i>0;i--)
            {
                var ghost=position-new V(p.Velocity.X,p.Velocity.Y)*i*.8f;
                Canvas2D.SpriteBatch.Draw(bodies[(int)p.Hero],ghost,new Rectangle(pose*12,0,12,20),Muted*(.24f-i*.05f),0,new V(6,20),1,effect,0);
            }
        Canvas2D.SpriteBatch.Draw(bodies[(int)p.Hero],position,new Rectangle(pose*12,0,12,20),tint,0,new V(6,20),1,effect,0);
        int head=pose==9?4:p.Aiming&&MathF.Sin(p.Aim)<-.5f?1:p.Aiming&&MathF.Sin(p.Aim)>.5f?2:0;
        int headHeight=pose>=10?15:pose==9?17:20;
        Canvas2D.SpriteBatch.Draw(heads[(int)p.Hero],position-new V(0,headHeight),new Rectangle(head*10,0,10,10),tint,0,new V(5,0),1,effect,0);
        if(p.Dodge==0)
        {
            var kind=p.Ammo.Count>0?p.Ammo[0]:(Equipment)p.Hero;
            float angle=p.Aiming?p.Aim:p.Facing<0?MathF.PI:0;
            var hand=position+new V(p.Facing*2,-9+(pose==9?3:0));
            var weaponEffect=MathF.Cos(angle)<0?SpriteEffects.FlipVertically:SpriteEffects.None;
            if(kind<Equipment.EmberVessel)
            {
                int weaponFrame=p.Ammo.Count==0?4:p.Aiming?0:2;
                Canvas2D.SpriteBatch.Draw(heldWeapons[(int)kind],hand,new Rectangle(weaponFrame*12,0,12,10),tint,angle,new V(2,4),1,weaponEffect,0);
            }
            else
            {
                var t=CombatArt.Weapon(kind);Canvas2D.SpriteBatch.Draw(t,hand+new V(MathF.Cos(angle)*5,MathF.Sin(angle)*5),null,tint,angle,new V(t.Width/2f,t.Height/2f),1,weaponEffect,0);
            }
            if(p.Aiming)
            {
                var direction=new V(MathF.Cos(angle),MathF.Sin(angle));var normal=new V(-direction.Y,direction.X);
                for(int i=16;i<35;i+=5)Canvas2D.Rect(position.X+direction.X*i,position.Y-8+direction.Y*i,1,1,p.Ammo.Count>0?Gold:Muted);
                var tip=position+new V(0,-8)+direction*37;
                RelicArt.Segment(tip-direction*3+normal*3,tip,Gold);RelicArt.Segment(tip,tip-direction*3-normal*3,Gold);
            }
        }
        if(p.Shield>0)for(int i=0;i<20;i++){float angle=i*MathF.Tau/20;Canvas2D.Rect(MathF.Round(position.X+MathF.Cos(angle)*11),MathF.Round(position.Y-9+MathF.Sin(angle)*13),1,1,Gold);}
        if(p.Sandals>0&&frame%8<4)for(int i=1;i<=3;i++)Canvas2D.Rect(position.X-p.Facing*i*3,position.Y-1-i%2,2,1,new Color(156,207,164)*(.8f-i*.15f));
        float ammoY=position.Y-headHeight-7;
        if(p.Ammo.Count==0)
        {
            Canvas2D.Rect(position.X-3,ammoY+1,7,4,Ink);RelicArt.Segment(new(position.X-2,ammoY+1),new(position.X+2,ammoY+4),Muted);
        }
        else for(int i=0;i<p.Ammo.Count;i++)
        {
            var ammo=RelicArt.AmmoIcon(p.Ammo[i]);float x=position.X-(p.Ammo.Count*4-1)/2f+i*4;
            Canvas2D.SpriteBatch.Draw(ammo,new V(MathF.Round(x),ammoY),Color.White);
            if(i==0)Canvas2D.Rect(MathF.Round(x),ammoY+6,3,1,Gold);
        }
        if(frame<120)Text($"P{p.Slot+1}",position.X,ammoY-9,Gold);
    }
    private void DrawMissile(Missile m)
    {
        var point=Point(m.Position);float angle=m.Direction;
        var t=CombatArt.Weapon(m.Kind);
        foreach(var offset in WrapOffsets)
        {
            var p=point+offset;if(p.X< -20||p.X>340||p.Y< -20||p.Y>260)continue;
            if(m.EnemyAttack is EnemyProjectileKind.Venom or EnemyProjectileKind.GroundWave)
            {
                if(m.EnemyAttack==EnemyProjectileKind.GroundWave)
                {
                    Canvas2D.Rect(p.X-4,p.Y-3,8,5,new Color(30,29,35));Canvas2D.Rect(p.X-3,p.Y-4,3,5,new Color(206,171,108));
                    Canvas2D.Rect(p.X+1,p.Y-2,3,3,new Color(153,114,75));Canvas2D.Rect(p.X-1,p.Y-5,2,3,new Color(239,216,154));
                }
                else
                {
                    Canvas2D.Rect(p.X-3,p.Y-2,6,4,new Color(30,29,35));Canvas2D.Rect(p.X-2,p.Y-1,4,2,new Color(157,184,73));
                    Canvas2D.Rect(p.X-1,p.Y-2,2,1,new Color(222,227,129));Canvas2D.Rect(p.X-Math.Sign(m.Velocity.X)*4,p.Y,2,1,new Color(99,123,49));
                }
                continue;
            }
            if(!m.Resting)for(int i=3;i>=1;i--){var tail=p-new V(m.Velocity.X,m.Velocity.Y)*(i*.45f);Canvas2D.Rect(MathF.Round(tail.X),MathF.Round(tail.Y),2,1,CombatArt.Tint(m.Kind)*(1-i*.22f));}
            Canvas2D.SpriteBatch.Draw(t,p,null,Color.White,angle,new V(t.Width/2f,t.Height/2f),1,SpriteEffects.None,0);
        }
    }
    private void Tiles(ArenaLayout layout,string[] grid,Texture2D texture,Color tint,Dictionary<string,int[]>? choicesByType=null)
    {
        bool Solid(int x,int y)=>grid[(y+24)%24][(x+32)%32]=='1';
        for(int y=0;y<24;y++)for(int x=0;x<32;x++)
        {
            if(!Solid(x,y))continue;bool l=Solid(x-1,y),r=Solid(x+1,y),u=Solid(x,y-1),d=Solid(x,y+1);
            string key=!l&&!r&&!u&&!d?"Single":!u&&!d?(l?(r?"SingleHorizontalCenter":"SingleHorizontalRight"):"SingleHorizontalLeft"):!l&&!r?(u?(d?"SingleVerticalCenter":"SingleVerticalBottom"):"SingleVerticalTop"):!u?(l?(r?"Top":"TopRight"):"TopLeft"):!d?(l?(r?"Bottom":"BottomRight"):"BottomLeft"):!l?"Left":!r?"Right":"Center";
            int[] choices=(choicesByType??layout.Tiles).GetValueOrDefault(key)??[0];int tile=choices.Length>0?choices[(x*7+y*13)%choices.Length]:0;
            Canvas2D.SpriteBatch.Draw(texture,new V(x*10,y*10),new Rectangle(tile%8*10,tile/8*10,10,10),tint);
        }
    }
    public void Message(string title,string line,string bottom="ESC BACK")
    {
        Sky();Panel(14,65,292,110);Text(title,160,81,Gold);
        var words=line.Split(' ');string row="";int y=104;
        foreach(var word in words){if(font.MeasureString(row+word).X>263){Text(row,160,y);y+=11;row="";}row+=word+" ";}Text(row,160,y);Text(bottom,160,158,Muted);
    }
    public void Controls()
    {
        Sky();Panel(10,20,300,201);Text("CONTROLS",160,33,Gold);
        string[] rows=["P1 ARROWS MOVE / AIM   C JUMP","X / Z HOLD AIM / RELEASE FIRE","LEFT SHIFT DODGE / CATCH   V CYCLE","P2 WASD MOVE / AIM   SPACE JUMP","J FIRE   K DODGE / CATCH   L CYCLE","GAMEPAD: LEFT STICK / DPAD MOVE","A JUMP   X FIRE   B DODGE   Y CYCLE","ESC / START PAUSE   F11 FULLSCREEN","M MUSIC   ESC RETURN"];
        for(int i=0;i<rows.Length;i++)Text(rows[i],160,54+i*17,i%2==0?Pale:Muted);
    }
    private static V Point(System.Numerics.Vector2 p)=>new(MathF.Round(p.X),MathF.Round(p.Y));
    public void Dispose(){icons.Dispose();CampaignEnemyArt.Dispose();CombatArt.Dispose();RelicArt.Dispose();foreach(var t in textures.Values.Concat(bodies).Concat(heads).Concat(heldWeapons).Concat(corpses))t.Dispose();Canvas2D.Pixel.Dispose();}
}
