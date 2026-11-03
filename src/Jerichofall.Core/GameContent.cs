using System.Security.Cryptography;
using System.Text.Json;

namespace Jerichofall;

public sealed class GameContent
{
    public string Root { get; }
    public CampaignCatalog Campaign { get; }
    public string Fingerprint { get; }
    private readonly Dictionary<string,string[][]> encounters;
    public GameContent(string root)
    {
        Root=Path.GetFullPath(root);Campaign=new(Root);
        encounters=GameJson.Read<Dictionary<string,string[][]>>(Path.Combine(Root,"encounters.json"));
        foreach(var chapter in Campaign.Chapters)if(!encounters.TryGetValue(chapter.Id,out var waves)||waves.Length!=3||waves.Any(w=>w.Length==0||w.Any(e=>!Enum.TryParse<EnemyKind>(e,out _))))throw new InvalidDataException("Missing campaign encounters.");
        var bytes=File.ReadAllBytes(Path.Combine(Root,"content-manifest.json"));
        using var manifest=JsonDocument.Parse(bytes);
        if(manifest.RootElement.GetProperty("schema").GetString()!="jerichofall-independent-content-v1")throw new InvalidDataException("Unsupported content manifest.");
        foreach(var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string relative=file.GetProperty("path").GetString()??"";
            if(relative.Contains('\\')||Path.IsPathRooted(relative)||relative.Split('/').Any(p=>p is ".." or "." or ""))throw new InvalidDataException("Unsafe content path.");
            using var stream=File.OpenRead(Path.Combine(Root,relative));
            if(Convert.ToHexStringLower(SHA256.HashData(stream))!=file.GetProperty("sha256").GetString())throw new InvalidDataException("Content is missing or changed: "+relative);
        }
        // Include our own simulation bytes: asset matches alone cannot establish compatible gameplay.
        using var own=File.OpenRead(typeof(Battle).Assembly.Location);
        Fingerprint=Convert.ToHexStringLower(SHA256.HashData(bytes.Concat(SHA256.HashData(own)).ToArray()));
    }
    public ArenaLayout Arena(string id)
    {
        if(id.Any(c=>!char.IsAsciiLetterLower(c)&&c!='-'))throw new ArgumentException("Invalid arena identifier.");
        var layout=GameJson.Read<ArenaLayout>(Path.Combine(Root,"arenas",id+".json"));layout.Validate();return layout;
    }
    public EnemyKind[][] Waves(string id)=>encounters[id].Select(w=>w.Select(Enum.Parse<EnemyKind>).ToArray()).ToArray();
    public Battle CampaignBattle(int chapter,Hero[] heroes)=>new(Arena(Campaign.Chapters[chapter].Id),heroes,BattleMode.Campaign,chapter,Waves(Campaign.Chapters[chapter].Id));
}
