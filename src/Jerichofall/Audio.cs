using Microsoft.Xna.Framework.Audio;

namespace Jerichofall.View;

internal sealed class Audio : IDisposable
{
    private readonly string root;
    private readonly Dictionary<string,SoundEffect> sounds=[];
    private SoundEffectInstance? music;
    private string cue="";
    private bool available=true;
    public bool Muted { get; private set; }
    public Audio(string root){this.root=root;}
    public void Toggle(){Muted=!Muted;if(music!=null)music.Volume=Muted?0:.4f;}
    public void Music(string name)
    {
        if(cue==name||!available)return;cue=name;
        try{music?.Dispose();music=Load("music/"+name).CreateInstance();music.IsLooped=true;music.Volume=Muted?0:.4f;music.Play();}
        catch(Exception e) when(e is NoAudioHardwareException or InvalidOperationException or DllNotFoundException){available=false;Console.Error.WriteLine("Audio unavailable: "+e.Message);}
    }
    private SoundEffect Load(string name)
    {
        if(sounds.TryGetValue(name,out var found))return found;
        using var stream=File.OpenRead(Path.Combine(root,"audio",name+".wav"));return sounds[name]=SoundEffect.FromStream(stream);
    }
    public void Play(string name)
    {
        if(Muted||!available)return;
        string path=Path.Combine(root,"audio/sfx",name+".wav");if(!File.Exists(path))return;
        try{Load("sfx/"+name).Play(.55f,0,0);}catch(NoAudioHardwareException){available=false;}
    }
    public void Events(IEnumerable<GameEvent> events)
    {
        foreach(var e in events.Take(12))
        {
            string sound=e.Kind;
            if(sound=="shoot")sound=(Equipment)e.Value switch{Equipment.Stone=>"sling-release",Equipment.Staff=>"staff-cast",Equipment.Jawbone=>"jawbone-swing",Equipment.Spear=>"spear-release",Equipment.EmberVessel=>"ember-throw",Equipment.BronzeDisc=>"disc-throw",_=>"storm-throw"};
            else if(sound=="death")sound="enemy-defeat";
            else if(sound=="spirit-rise")sound="staff-cast";
            else if(sound=="spirit-dash")sound="dodge";
            else if(sound=="spirit-banish")sound="shield-break";
            Play(sound);
        }
    }
    public void Dispose(){music?.Dispose();foreach(var value in sounds.Values)value.Dispose();}
}
