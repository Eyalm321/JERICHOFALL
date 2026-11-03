using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Jerichofall;

public sealed class WireMessage
{
    public string Type { get; set; }="";
    public string Fingerprint { get; set; }="";
    public string Arena { get; set; }="";
    public string Error { get; set; }="";
    public int Version { get; set; }=1;
    public int Slot { get; set; }
    public int Players { get; set; }
    public Hero Hero { get; set; }
    public long Sequence { get; set; }
    public PlayerInput Input { get; set; }
    public BattleState? State { get; set; }
}

internal static class Wire
{
    public const int MaximumBytes=512*1024;
    public static byte[] Encode(WireMessage message)
    {
        var json=JsonSerializer.SerializeToUtf8Bytes(message,GameJson.Options);
        if(json.Length>MaximumBytes)throw new InvalidDataException("Network state too large.");
        var bytes=new byte[4+json.Length];BinaryPrimitives.WriteInt32BigEndian(bytes,json.Length);json.CopyTo(bytes,4);return bytes;
    }
    public static async Task<WireMessage> Read(NetworkStream stream,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var header=new byte[4];await stream.ReadExactlyAsync(header,deadline.Token);
        int size=BinaryPrimitives.ReadInt32BigEndian(header);if(size<2||size>MaximumBytes)throw new InvalidDataException("Invalid network frame size.");
        var bytes=new byte[size];await stream.ReadExactlyAsync(bytes,deadline.Token);
        var message=JsonSerializer.Deserialize<WireMessage>(bytes,GameJson.Options)??throw new InvalidDataException("Empty network message.");
        if(message.Version!=1)throw new InvalidDataException("Incompatible protocol.");return message;
    }
}

internal sealed class Connection : IDisposable
{
    public TcpClient Client { get; }
    public NetworkStream Stream { get; }
    private readonly Channel<byte[]> outgoing=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
    private readonly CancellationTokenSource stop=new();
    public string? Error { get; private set; }
    public Connection(TcpClient client)
    {
        Client=client;Client.NoDelay=true;Stream=client.GetStream();_ = Task.Run(WriteLoop);
    }
    public void Send(byte[] bytes)=>outgoing.Writer.TryWrite(bytes);
    private async Task WriteLoop()
    {
        try{await foreach(var data in outgoing.Reader.ReadAllAsync(stop.Token)){using var limit=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);limit.CancelAfter(5000);await Stream.WriteAsync(data,limit.Token);}}
        catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException){if(!stop.IsCancellationRequested)Error="Connection lost: "+e.GetType().Name;}
    }
    public void Dispose(){stop.Cancel();outgoing.Writer.TryComplete();Client.Dispose();}
}

/// <summary>Authoritative host: only this process simulates. Clients send bounded inputs and render snapshots.</summary>
public sealed class OnlineHost : IDisposable
{
    private sealed class Guest(int slot,Hero hero,Connection connection)
    {
        public int Slot=slot;public Hero Hero=hero;public Connection Connection=connection;public PlayerInput Input;public long Sequence=-1;public bool Ready;
    }
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop=new();
    private readonly object gate=new();
    private readonly Dictionary<int,Guest> guests=[];
    private readonly string fingerprint,arena;
    private readonly int players;
    private readonly Hero hero;
    private bool started;
    private int activeSockets;
    private string? error;
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public int Connected {get{lock(gate)return guests.Values.Count(g=>g.Ready)+1;}}
    public bool Ready=>Connected==players;
    public string? Error {get{lock(gate)return error??guests.Values.Select(g=>g.Connection.Error).FirstOrDefault(e=>e!=null);}}
    public OnlineHost(string fingerprint,string arena,int players,Hero hero,int port=28060,bool loopback=false)
    {
        if(players is <2 or >4)throw new ArgumentOutOfRangeException(nameof(players));
        this.fingerprint=fingerprint;this.arena=arena;this.players=players;this.hero=hero;
        listener=new(loopback?IPAddress.Loopback:IPAddress.Any,port);listener.Start(8);_ = Task.Run(AcceptLoop);
    }
    private async Task AcceptLoop()
    {
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);if(Interlocked.Increment(ref activeSockets)>8){Interlocked.Decrement(ref activeSockets);client.Dispose();continue;}_ = Accept(client);}}
        catch(Exception e) when(e is SocketException or OperationCanceledException or ObjectDisposedException){if(!stop.IsCancellationRequested)error="Could not accept connections.";}
    }
    private async Task Accept(TcpClient socket)
    {
        Connection? connection=null;Guest? guest=null;
        try
        {
            socket.NoDelay=true;var stream=socket.GetStream();var hello=await Wire.Read(stream,stop.Token);
            if(hello.Type!="hello"||hello.Fingerprint!=fingerprint||!Enum.IsDefined(hello.Hero))
            {await stream.WriteAsync(Wire.Encode(new(){Type="error",Error="Build or content mismatch. Use the same JERICHOFALL release."}),stop.Token);return;}
            int slot;
            lock(gate)
            {
                slot=Enumerable.Range(1,players-1).FirstOrDefault(s=>!guests.ContainsKey(s),-1);
                if(started||slot<0)slot=-1;
                else {connection=new(socket);guest=new(slot,hello.Hero,connection);guests.Add(slot,guest);}
            }
            if(slot<0){await stream.WriteAsync(Wire.Encode(new(){Type="error",Error="Lobby is full or the match has started."}),stop.Token);return;}
            await stream.WriteAsync(Wire.Encode(new(){Type="welcome",Slot=slot,Players=players,Arena=arena,Fingerprint=fingerprint}),stop.Token);
            lock(gate)guest!.Ready=true;
            while(!stop.IsCancellationRequested)
            {
                var message=await Wire.Read(stream,stop.Token);
                if(message.Type!="input")throw new InvalidDataException("Expected an input message.");message.Input.Validate();
                lock(gate)
                {
                    if(message.Sequence<=guest!.Sequence)throw new InvalidDataException("Stale input sequence.");
                    guest.Sequence=message.Sequence;guest.Input=message.Input;
                }
            }
        }
        catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or JsonException or InvalidDataException)
        {if(guest!=null&&!stop.IsCancellationRequested){lock(gate){if(started)error="Player "+(guest.Slot+1)+" disconnected. Return to the lobby.";}}}
        finally{lock(gate){if(guest!=null)guests.Remove(guest.Slot);}connection?.Dispose();socket.Dispose();Interlocked.Decrement(ref activeSockets);}
    }
    public Hero[] StartRoster()
    {
        lock(gate){if(guests.Values.Count(g=>g.Ready)!=players-1)throw new InvalidOperationException("Lobby is not ready.");started=true;return [hero,..guests.Values.OrderBy(g=>g.Slot).Select(g=>g.Hero)];}
    }
    public PlayerInput[] Inputs(PlayerInput local)
    {
        var result=new PlayerInput[players];result[0]=local;
        lock(gate)foreach(var g in guests.Values)result[g.Slot]=g.Input;return result;
    }
    public void Broadcast(BattleState state)
    {
        var bytes=Wire.Encode(new(){Type="state",State=state});lock(gate)foreach(var guest in guests.Values)guest.Connection.Send(bytes);
    }
    public void Heartbeat()
    {
        var bytes=Wire.Encode(new(){Type="lobby"});lock(gate)foreach(var guest in guests.Values.Where(g=>g.Ready))guest.Connection.Send(bytes);
    }
    public void Dispose(){stop.Cancel();listener.Stop();lock(gate)foreach(var g in guests.Values.ToArray())g.Connection.Dispose();}
}

public sealed class OnlineClient : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private Connection? connection;
    private readonly string fingerprint;
    private long sequence;
    private volatile BattleState? state;
    public BattleState? State=>state;
    public int Slot { get; private set; }=-1;
    public string Arena { get; private set; }="";
    public string? Error { get; private set; }
    public bool Connected=>Slot>0&&Error==null;
    public OnlineClient(string fingerprint,string host,int port,Hero hero)
    {
        this.fingerprint=fingerprint;_ = Task.Run(()=>Connect(host,port,hero));
    }
    private async Task Connect(string host,int port,Hero hero)
    {
        var client=new TcpClient();
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(10000);
            await client.ConnectAsync(host,port,deadline.Token);client.NoDelay=true;var stream=client.GetStream();
            await stream.WriteAsync(Wire.Encode(new(){Type="hello",Fingerprint=fingerprint,Hero=hero}),stop.Token);
            var welcome=await Wire.Read(stream,stop.Token);
            if(welcome.Type=="error")throw new InvalidDataException(welcome.Error);
            if(welcome.Type!="welcome"||welcome.Fingerprint!=fingerprint||welcome.Slot is <1 or >3||welcome.Players is <2 or >4||welcome.Slot>=welcome.Players||welcome.Arena is null or ""||welcome.Arena.Length>60||welcome.Arena.Any(c=>!char.IsAsciiLetterLower(c)&&c!='-'))throw new InvalidDataException("Invalid host handshake.");
            connection=new(client);Arena=welcome.Arena;Slot=welcome.Slot;
            while(!stop.IsCancellationRequested)
            {
                var message=await Wire.Read(stream,stop.Token);
                if(message.Type=="lobby")continue;
                if(message.Type!="state"||message.State==null)throw new InvalidDataException("Invalid host state.");
                Validate(message.State);state=message.State;
            }
        }
        catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or JsonException or InvalidDataException)
        {if(!stop.IsCancellationRequested)Error=e is InvalidDataException?e.Message:"Connection lost or timed out. Return and try joining again.";}
        finally{connection?.Dispose();client.Dispose();}
    }
    private static void Validate(BattleState state)
    {
        if(state.Mode!=BattleMode.Versus||state.Frame<0||state.EffectFrame<0||!Enum.IsDefined(state.Phase)||state.Fighters is not {Count:>=2 and <=4}||state.Missiles is not {Count:<=256}||state.Supplies is not {Count:<=128}||state.Arrivals is not {Count:0}||state.Events is not {Count:<=512}||state.Scores is not {Length:4})throw new InvalidDataException("Host state exceeds limits.");
        if(state.Fighters.Any(p=>p==null||!p.IsPlayer||p.Slot>3||!Enum.IsDefined(p.Hero)||!ArenaLayout.Finite(p.Position)||!ArenaLayout.Finite(p.Velocity)||!float.IsFinite(p.Aim)||!Enum.IsDefined(p.Motion.Mode)||p.Brain==null||!Enum.IsDefined(p.Brain.Phase)||!ArenaLayout.Finite(p.PixelRemainder)||!ArenaLayout.Finite(p.Motion.Velocity)||p.Ammo is not {Count:<=6}||p.Ammo.Any(a=>!Enum.IsDefined(a)||a>=Equipment.Manna))
            ||state.Missiles.Any(m=>m==null||!ArenaLayout.Finite(m.Position)||!ArenaLayout.Finite(m.Velocity)||!Enum.IsDefined(m.Kind)||!Enum.IsDefined(m.EnemyAttack)||!float.IsFinite(m.Direction)||!ArenaLayout.Finite(m.PixelRemainder)||m.Kind>=Equipment.Manna)
            ||state.Supplies.Any(s=>s==null||!ArenaLayout.Finite(s.Position)||!Enum.IsDefined(s.Kind))
            ||state.Events.Any(e=>e==null||e.Kind is null||e.Kind.Length>64||!ArenaLayout.Finite(e.Position)))throw new InvalidDataException("Invalid host entities.");
        if(state.Fighters.Select(p=>p.Slot).Distinct().Count()!=state.Fighters.Count)throw new InvalidDataException("Duplicate player slots.");
        foreach(var spirit in state.Fighters.Select(p=>p.Spirit).OfType<DeathSpirit>())
            if(!Enum.IsDefined(spirit.Phase)||spirit.Age<0||spirit.PhaseAge<0||spirit.PhaseAge>1000||spirit.Warmup is <0 or >30||spirit.Cooldown is <0 or >30||spirit.Happy is <0 or >30||spirit.Facing is not (-1 or 1)||spirit.CorpseFacing is not (-1 or 1)||!float.IsFinite(spirit.CruiseSpeed)||spirit.CruiseSpeed is <0 or >3||
                new[]{spirit.Corpse,spirit.Position,spirit.Velocity,spirit.Remainder,spirit.LastInput}.Any(p=>!ArenaLayout.Finite(p)||Math.Abs(p.X)>1000||Math.Abs(p.Y)>1000))throw new InvalidDataException("Invalid host spirit.");
    }
    public void Send(PlayerInput input)
    {
        input.Validate();connection?.Send(Wire.Encode(new(){Type="input",Sequence=Interlocked.Increment(ref sequence),Input=input}));
        if(connection?.Error!=null)Error=connection.Error;
    }
    public void Dispose(){stop.Cancel();connection?.Dispose();}
}
