using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using V=Microsoft.Xna.Framework.Vector2;

namespace Jerichofall.View;

internal sealed record GameOptions(string Content,string Save,bool Exodus,bool Versus,int Players,string Arena,int Hero,int? Host,string? Join,int Frames,string? Verify)
{
    public static GameOptions Parse(string[] args)
    {
        var values=new Dictionary<string,string>();var flags=new HashSet<string>();
        foreach(string unknown in args.Where(a=>a.StartsWith("--")&&!new[]{"--content","--save","--exodus","--versus","--players","--arena","--hero","--host","--join","--frames","--verify"}.Contains(a)))throw new ArgumentException("Unknown argument: "+unknown);
        for(int i=0;i<args.Length;i++)
        {
            if(args[i] is "--exodus" or "--versus")flags.Add(args[i]);
            else if(i+1<args.Length){values[args[i]]=args[i+1];i++;}else throw new ArgumentException("Missing argument value.");
        }
        int players=int.Parse(values.GetValueOrDefault("--players","2")),hero=int.Parse(values.GetValueOrDefault("--hero","0"));
        if(players is <1 or >4||hero is <0 or >3)throw new ArgumentException("Players must be 1-4 and hero 0-3.");
        string content=Path.GetFullPath(values.GetValueOrDefault("--content",Path.Combine(AppContext.BaseDirectory,"Content")));
        string save=Path.GetFullPath(values.GetValueOrDefault("--save",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JERICHOFALL","standalone-campaign.json")));
        return new(content,save,flags.Contains("--exodus"),flags.Contains("--versus"),players,values.GetValueOrDefault("--arena","jerusalem"),hero,values.ContainsKey("--host")?int.Parse(values["--host"]):null,values.GetValueOrDefault("--join"),int.Parse(values.GetValueOrDefault("--frames","0")),values.GetValueOrDefault("--verify"));
    }
}
internal enum Screen { Title, Heroes, Map, Briefing, Battle, Controls, Join, Lobby, Error, Restart, Ending }
internal enum StartMode { Campaign, Local, Host }

internal sealed class JerichoGame : Game
{
    private readonly GameOptions options;
    private readonly GraphicsDeviceManager graphics;
    private readonly Controls controls=new();
    private GameContent data=null!;
    private CampaignSave save=null!;
    private CampaignProgress progress=null!;
    private Renderer renderer=null!;
    private SpriteBatch batch=null!;
    private RenderTarget2D canvas=null!;
    private Audio audio=null!;
    private Screen screen=Screen.Title;
    private StartMode startMode=StartMode.Campaign;
    private int menu,chapter,hero,second=1,players=1,age;
    private string arena="jerusalem",address="127.0.0.1:28060",error="";
    private Battle? battle;
    private ArenaLayout? layout;
    private OnlineHost? host;
    private OnlineClient? client;
    private long heardFrame=-1;
    private bool savedClear;
    private readonly HashSet<string> captures=[];
    private readonly HashSet<string> visited=[];
    private int previousWidth,previousHeight;
    public JerichoGame(GameOptions options)
    {
        this.options=options;graphics=new(this){PreferredBackBufferWidth=960,PreferredBackBufferHeight=720,SynchronizeWithVerticalRetrace=true};
        // Verification captures specific simulation frames, so each scripted
        // update must reach Draw even when the runner cannot maintain 60 Hz.
        IsFixedTimeStep=options.Verify==null;TargetElapsedTime=TimeSpan.FromTicks(TimeSpan.TicksPerSecond/60);IsMouseVisible=true;
        Window.Title="JERICHOFALL";Window.AllowUserResizing=true;InactiveSleepTime=TimeSpan.Zero;
        Window.ClientSizeChanged+=(_,_)=>Resize();
    }
    protected override void LoadContent()
    {
        data=new(options.Content);save=new(options.Save,Console.Error.WriteLine);progress=save.Load();
        Content.RootDirectory=options.Content;batch=new(GraphicsDevice);canvas=new(GraphicsDevice,320,240,false,SurfaceFormat.Color,DepthFormat.None,0,RenderTargetUsage.PreserveContents);
        renderer=new(GraphicsDevice,batch,Content,options.Content);audio=new(options.Content);audio.Music("overture");
        hero=options.Hero;arena=options.Arena;chapter=progress.UnlockedThrough;
        if(options.Exodus){players=1;screen=Screen.Map;hero=progress.Hero;}
        if(options.Versus){players=Math.Max(2,options.Players);StartVersus();}
        if(options.Host.HasValue){players=Math.Max(2,options.Players);StartHost(options.Host.Value);}
        if(options.Join!=null){address=options.Join;Join();}
        TextInputEXT.TextInput+=TextEntered;
    }
    private void Resize()
    {
        var b=Window.ClientBounds;if(b.Width<320||b.Height<240||graphics.IsFullScreen)return;
        if(b.Width==previousWidth&&b.Height==previousHeight)return;
        previousWidth=b.Width;previousHeight=b.Height;graphics.PreferredBackBufferWidth=b.Width;graphics.PreferredBackBufferHeight=b.Height;
    }
    private void TextEntered(char c)
    {
        if(screen!=Screen.Join)return;
        if(c=='\b'){if(address.Length>0)address=address[..^1];}
        else if(address.Length<120&&(char.IsAsciiLetterOrDigit(c)||c is '.' or ':' or '-' or '[' or ']'))address+=c;
    }
    protected override void Update(GameTime gameTime)
    {
        age++;controls.Poll();
        if(controls.Press(Keys.F11)){graphics.ToggleFullScreen();}
        if(controls.Press(Keys.M))audio.Toggle();
        if(options.Verify!=null)VerifyFlow();
        visited.Add(screen.ToString());
        try
        {
            switch(screen)
            {
                case Screen.Title:
                    int direction=controls.MenuDown?1:controls.MenuUp?-1:0;
                    if(direction!=0){menu=(menu+direction+5)%5;audio.Play("ui-move");}
                    if(controls.Confirm)
                    {
                        audio.Play("ui-confirm");if(menu==3){screen=Screen.Join;TextInputEXT.StartTextInput();}else if(menu==4)screen=Screen.Controls;
                        else{startMode=(StartMode)menu;players=menu==0?1:2;screen=Screen.Heroes;}
                    }
                    if(controls.Back)Exit();break;
                case Screen.Heroes:
                    if(controls.MenuLeft||controls.MenuRight){hero=(hero+(controls.MenuRight?1:3))%4;audio.Play("ui-move");}
                    if(controls.Press(Keys.Q))second=(second+3)%4;if(controls.Press(Keys.E))second=(second+1)%4;
                    if(controls.Press(Keys.Tab))players=startMode==StartMode.Campaign?3-players:players==4?2:players+1;
                    if(controls.Press(Keys.F7))NextArena();
                    if(controls.Confirm){progress.Hero=hero;save.Write(progress);if(startMode==StartMode.Campaign)screen=Screen.Map;else if(startMode==StartMode.Host)StartHost(28060);else StartVersus();}
                    if(controls.Back)screen=Screen.Title;break;
                case Screen.Map:
                    if(controls.MenuRight||controls.MenuDown){chapter=(chapter+1)%18;audio.Play("ui-move");}
                    if(controls.MenuLeft||controls.MenuUp){chapter=(chapter+17)%18;audio.Play("ui-move");}
                    if(controls.Confirm&&progress.IsUnlocked(chapter)){screen=Screen.Briefing;audio.Play("ui-confirm");}
                    if(controls.Press(Keys.H)){startMode=StartMode.Campaign;players=Math.Clamp(players,1,2);screen=Screen.Heroes;}
                    if(controls.Press(Keys.N))screen=Screen.Restart;
                    if(controls.Back)screen=Screen.Title;break;
                case Screen.Briefing:if(controls.Confirm)StartCampaign();if(controls.Back)screen=Screen.Map;break;
                case Screen.Battle:AdvanceBattle();break;
                case Screen.Join:
                    if(controls.Press(Keys.F7))hero=(hero+1)%4;
                    if(controls.Confirm){TextInputEXT.StopTextInput();Join();}
                    if(controls.Back){TextInputEXT.StopTextInput();screen=Screen.Title;}break;
                case Screen.Lobby:
                    client?.Send(default);host?.Heartbeat();
                    if(host?.Ready==true){layout=data.Arena(arena);battle=new(layout,host.StartRoster(),BattleMode.Versus);screen=Screen.Battle;audio.Music("canaan");}
                    else if(client?.State!=null){arena=client.Arena;layout=data.Arena(arena);screen=Screen.Battle;audio.Music("canaan");}
                    CheckNetwork();if(controls.Back)ReturnToMenu();break;
                case Screen.Controls:case Screen.Error:if(controls.Back||controls.Confirm)ReturnToMenu();break;
                case Screen.Restart:
                    if(controls.Confirm){if(save.Archive()){progress=new(){Hero=hero};save.Write(progress);chapter=0;screen=Screen.Map;}else{error=save.Error??"Could not preserve your save.";screen=Screen.Error;}}
                    if(controls.Back)screen=Screen.Map;break;
                case Screen.Ending:if(controls.Confirm||controls.Back)screen=Screen.Map;break;
            }
        }
        catch(Exception e) when(e is IOException or System.Net.Sockets.SocketException or ArgumentException or InvalidOperationException)
        {error=e.Message;CloseNetwork();screen=Screen.Error;Console.Error.WriteLine(e);}
        if(options.Frames>0&&age>=options.Frames){WriteVerification();Exit();}
        base.Update(gameTime);
    }
    private Hero[] Roster()=>Enumerable.Range(0,players).Select(i=>(Hero)(i==0?hero:i==1?second:i)).ToArray();
    private void StartCampaign()
    {
        if(!progress.IsUnlocked(chapter))throw new InvalidOperationException("Checkpoint is locked.");
        CloseNetwork();players=Math.Clamp(players,1,2);battle=data.CampaignBattle(chapter,Roster());layout=battle.Layout;screen=Screen.Battle;savedClear=false;
        audio.Music(chapter<3?"egypt":chapter<6?"wilderness":chapter<8?"sinai":chapter<15?"wilderness":"canaan");
    }
    private void StartVersus(){CloseNetwork();players=Math.Clamp(players,2,4);layout=data.Arena(arena);battle=new(layout,Roster(),BattleMode.Versus);screen=Screen.Battle;audio.Music("canaan");}
    private void StartHost(int port){CloseNetwork();host=new(data.Fingerprint,arena,players,(Hero)hero,port);screen=Screen.Lobby;}
    private void Join()
    {
        CloseNetwork();string machine=address;int port=28060;
        int split=address.LastIndexOf(':');if(split>0&&int.TryParse(address[(split+1)..],out int parsed)){machine=address[..split];port=parsed;}
        if(string.IsNullOrWhiteSpace(machine)||port is <1 or >65535)throw new ArgumentException("Enter a hostname or IP address and optional :port.");
        client=new(data.Fingerprint,machine.Trim('[',']'),port,(Hero)hero);screen=Screen.Lobby;
    }
    private void AdvanceBattle()
    {
        PlayerInput local=IsActive?controls.Player(0):default;
        if(options.Verify!=null)local=Scripted(age,0);
        if(client!=null)client.Send(local);
        else if(battle!=null)
        {
            var inputs=host?.Inputs(local)??Enumerable.Range(0,players).Select(i=>options.Verify!=null?Scripted(age,i):IsActive?controls.Player(i):default).ToArray();
            battle.Step(inputs);host?.Broadcast(battle.State);
        }
        var state=client?.State??battle?.State;if(state==null)return;
        if(state.Frame!=heardFrame){audio.Events(state.Events);heardFrame=state.Frame;}
        CheckNetwork();
        if(state.Mode==BattleMode.Campaign&&state.Phase==BattlePhase.Victory&&!savedClear)
        {progress.Complete(chapter,state.Frame*TimeSpan.TicksPerSecond/60);save.Write(progress);savedClear=true;}
        if(state.Mode==BattleMode.Campaign&&controls.Confirm)
        {
            if(state.Phase==BattlePhase.Victory){screen=progress.Finished?Screen.Ending:Screen.Map;chapter=progress.UnlockedThrough;audio.Music("overture");}
            else if(state.Phase==BattlePhase.Defeat)StartCampaign();
        }
        if(controls.Press(Keys.Back)&& (state.Paused||state.Phase is BattlePhase.Victory or BattlePhase.Defeat or BattlePhase.RoundOver or BattlePhase.MatchOver))ReturnToMenu();
    }
    private void CheckNetwork(){var reason=host?.Error??client?.Error;if(reason!=null){error=reason;CloseNetwork();screen=Screen.Error;}}
    private void CloseNetwork(){host?.Dispose();host=null;client?.Dispose();client=null;}
    private void ReturnToMenu(){CloseNetwork();battle=null;screen=Screen.Title;audio.Music("overture");}
    private void NextArena(){string[] choices=["jerusalem","carmel","negev"];arena=choices[(Array.IndexOf(choices,arena)+1)%3];}
    internal static PlayerInput Scripted(int frame,int slot)
    {
        if(slot==0&&frame is >=795 and <=820)return new(1,-1,frame==810?Jerichofall.Buttons.Dodge:0);
        int cycle=(frame+slot*37)%180;var b=cycle%53<13?Jerichofall.Buttons.Jump:0;
        if(cycle%49<18)b|=Jerichofall.Buttons.Fire;if(cycle%117<6)b|=Jerichofall.Buttons.Dodge;
        return new(cycle<90?1:-1,cycle%70<10?-1:0,b);
    }
    private void VerifyFlow()
    {
        // Explicit rendering fixture, with an isolated --save path. Never enabled by ordinary play.
        if(age==1)screen=Screen.Title;
        if(age==65)screen=Screen.Heroes;
        if(age==130)screen=Screen.Map;
        if(age==195)screen=Screen.Briefing;
        if(age==260){chapter=0;players=2;StartCampaign();}
        if(age==450&&battle!=null)VerifyDeath(battle.State.Fighters[0]);
        if(age==500&&battle!=null)battle.State.Paused=true;
        if(age==540&&battle!=null)battle.State.Paused=false;
        if(age==600){players=4;StartVersus();}
        if(age==710&&battle!=null)VerifyDeath(battle.State.Fighters[0]);
        if(age==830)
        {
            chapter=0;players=1;StartCampaign();
            battle!.State.Fighters[0].Lives=1;VerifyDeath(battle.State.Fighters[0]);
        }
        if(age==880)screen=Screen.Controls;
    }
    private void VerifyDeath(Fighter fighter)
    {
        fighter.Invincible=0;fighter.Shield=0;fighter.Dodge=0;battle!.Damage(fighter);
    }
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(canvas);GraphicsDevice.Clear(Renderer.Ink);
        batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
        switch(screen)
        {
            case Screen.Title:renderer.Title(menu);break;
            case Screen.Heroes:renderer.Heroes(hero,second,players,startMode==StartMode.Campaign,arena);break;
            case Screen.Map:renderer.Map(data.Campaign,progress,chapter,age);break;
            case Screen.Briefing:renderer.Briefing(data.Campaign.Chapters[chapter],chapter);break;
            case Screen.Battle:
                var state=client?.State??battle?.State;if(state!=null&&layout!=null)renderer.Battle(layout,state);break;
            case Screen.Join:renderer.Message("JOIN AS "+((Hero)hero).ToString().ToUpperInvariant(),address+"_","F7 HERO   ENTER JOIN   ESC BACK");break;
            case Screen.Lobby:renderer.Message("ONLINE VERSUS",host!=null?$"WAITING FOR PLAYERS {host.Connected}/{players} - TCP PORT {host.Port}":"CONNECTING TO "+address);break;
            case Screen.Error:renderer.Message("COULD NOT CONTINUE",error);break;
            case Screen.Restart:renderer.Message("BEGIN A NEW JOURNEY?","YOUR CURRENT JOURNEY WILL BE ARCHIVED.","ENTER RESTART   ESC KEEP JOURNEY");break;
            case Screen.Ending:renderer.Message("THE ROAD TO CANAAN","EIGHTEEN CHECKPOINTS COMPLETE. THE JOURNEY LIVES ON.","ENTER RETURN TO THE MAP");break;
            case Screen.Controls:renderer.Controls();break;
        }
        if(save.Error!=null)renderer.Text(save.Error,160,238,Color.Orange);
        batch.End();GraphicsDevice.SetRenderTarget(null);GraphicsDevice.Clear(Color.Black);
        var bounds=GraphicsDevice.PresentationParameters;float scale=Math.Min(bounds.BackBufferWidth/320f,bounds.BackBufferHeight/240f);
        if(scale>=1)scale=MathF.Floor(scale);
        int w=(int)(320*scale),h=(int)(240*scale);
        batch.Begin(SpriteSortMode.Deferred,BlendState.Opaque,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
        batch.Draw(canvas,new Rectangle((bounds.BackBufferWidth-w)/2,(bounds.BackBufferHeight-h)/2,w,h),Color.White);batch.End();
        if(options.Verify!=null)
        {
            string name=screen==Screen.Battle?(battle?.State.Mode==BattleMode.Campaign?"Campaign":"Versus")+(battle?.State.Paused==true?"-Paused":""):screen.ToString();
            if(screen==Screen.Battle&&battle?.State.Phase==BattlePhase.Fighting&&!battle.State.Paused)name+="-Fighting";
            if((age%65==45||age==520||age==740)&&captures.Add(name)){Directory.CreateDirectory(options.Verify);using var f=File.Create(Path.Combine(options.Verify,name+".png"));canvas.SaveAsPng(f,320,240);}
            string? deathCapture=age switch{470=>"Campaign-Spirit",566=>"Campaign-Respawn",800=>"Versus-Spirit",812=>"Versus-Spirit-Dash",860=>"Campaign-Last-Life-Spirit",_=>null};
            if(deathCapture!=null&&captures.Add(deathCapture))
            {
                var p=battle!.State.Fighters[0];
                bool expected=age switch{566=>p.Alive&&p.Spirit?.Phase==SpiritPhase.Dispersing,812=>p.Spirit?.Phase==SpiritPhase.Dashing,_=>p.Spirit?.Visible==true};
                if(!expected)throw new InvalidOperationException("Missing verification state: "+deathCapture);
                Directory.CreateDirectory(options.Verify);using var f=File.Create(Path.Combine(options.Verify,deathCapture+".png"));canvas.SaveAsPng(f,320,240);
            }
        }
        base.Draw(gameTime);
    }
    private void WriteVerification()
    {
        if(options.Verify==null)return;
        Directory.CreateDirectory(options.Verify);
        File.WriteAllText(Path.Combine(options.Verify,"render-checks.json"),JsonSerializer.Serialize(new{frames=age,screens=visited.Order().ToArray(),captures=captures.Order().ToArray(),contentFingerprint=data.Fingerprint,engine="Jerichofall.Core",assemblies=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).Order().ToArray(),fixture="Scripted inputs and screen transitions; no claim of human balance or complete gameplay parity."},new JsonSerializerOptions{WriteIndented=true}));
    }
    protected override void UnloadContent(){TextInputEXT.TextInput-=TextEntered;CloseNetwork();audio?.Dispose();renderer?.Dispose();canvas?.Dispose();batch?.Dispose();base.UnloadContent();}
}
