using System.Text.Json;

namespace Jerichofall;

public sealed record CampaignChapter(string Id, string Title, string Region, int X, int Y,
    string Subtitle, string[] Intro, string[] Outro);

public sealed class CampaignCatalog
{
    public static IReadOnlyList<string> RouteIds { get; } = Array.AsReadOnly(new[]
    {
        "rameses", "succoth", "sea-crossing", "marah", "elim", "dophkah", "rephidim", "sinai", "paran",
        "wilderness-camps", "ezion-geber", "kadesh", "edom", "moab", "nebo", "jordan", "jericho", "ai"
    });
    public static IReadOnlyList<string> LegacyRouteIds { get; } = Array.AsReadOnly(new[]
    {
        "rameses", "succoth", "sea-crossing", "marah", "elim", "rephidim", "sinai", "paran", "kadesh", "moab", "jordan", "jericho"
    });
    public static int ChapterCount => RouteIds.Count;
    public CampaignChapter[] Chapters { get; }
    public string Directory { get; }
    public CampaignCatalog(string directory)
    {
        Directory = directory;
        Chapters = JsonSerializer.Deserialize<CampaignChapter[]>(File.ReadAllText(Path.Combine(directory, "chapters.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (Chapters.Length != ChapterCount || !Chapters.Select(c => c.Id).SequenceEqual(RouteIds))
            throw new InvalidDataException($"Exodus requires the {ChapterCount} ordered route checkpoints.");
        foreach (var chapter in Chapters)
        {
            if (chapter.Id.Any(c => !char.IsAsciiLetterLower(c) && c != '-') || chapter.Intro.Length != 2 || chapter.Outro.Length != 2)
                throw new InvalidDataException("Invalid Exodus chapter: " + chapter.Id);
            if (!File.Exists(ArenaPath(chapter))) throw new FileNotFoundException("Missing campaign arena", ArenaPath(chapter));
        }
    }
    public string ArenaPath(CampaignChapter chapter) => Path.Combine(Directory, "arenas", chapter.Id + ".json");
}
