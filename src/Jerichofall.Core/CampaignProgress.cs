using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jerichofall;

public sealed class CampaignProgress
{
    public int Version { get; set; } = 2;
    public string[] ChapterIds { get; set; } = CampaignCatalog.RouteIds.ToArray();
    public bool[] Completed { get; set; } = new bool[CampaignCatalog.ChapterCount];
    public long[] BestTicks { get; set; } = new long[CampaignCatalog.ChapterCount];
    public int Hero { get; set; } = 1;
    [JsonIgnore] public int CompletedCount => Completed.Count(value => value);
    // The next chronological stop can precede a later victory retained from an older route.
    [JsonIgnore] public int UnlockedThrough => Array.FindIndex(Completed, value => !value) is var index && index >= 0
        ? index : Completed.Length - 1;
    [JsonIgnore] public bool Finished => Completed.Length == CampaignCatalog.ChapterCount && Completed.All(value => value);
    public bool IsUnlocked(int index) => index >= 0 && index < Completed.Length && (Completed[index] || index <= UnlockedThrough);

    public void Complete(int index, long elapsedTicks)
    {
        if (!IsUnlocked(index)) throw new InvalidOperationException("This checkpoint is locked.");
        Completed[index] = true;
        if (BestTicks[index] == 0 || elapsedTicks < BestTicks[index]) BestTicks[index] = Math.Max(1, elapsedTicks);
    }
}

public sealed class CampaignSave(string path, Action<string> log)
{
    private sealed class SaveDocument
    {
        public int Version { get; set; }
        public string[]? ChapterIds { get; set; }
        public bool[]? Completed { get; set; }
        public long[]? BestTicks { get; set; }
        public int Hero { get; set; } = -1;
    }

    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string? Error { get; private set; }
    public string? MigrationArchivePath { get; private set; }

    public CampaignProgress Load()
    {
        Error = null;
        foreach (var candidate in new[] { Path, Path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var data = Read(candidate, out var migrated);
                if (migrated)
                {
                    // Preserve both old files byte-for-byte before touching the primary.
                    // Existing backups are never reused as migration archive names.
                    if (PreserveLegacyFiles() && WriteCore(data, preserveRollingBackup: true))
                    {
                        log("Exodus save migrated to eighteen checkpoints; original save files preserved in unique archives.");
                        if (candidate != Path) Error = "BACKUP SAVE RESTORED";
                    }
                }
                else if (candidate != Path) Error = "BACKUP SAVE RESTORED";
                return data;
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                Error = "SAVE COULD NOT BE READ";
                log(candidate + ": " + ex.Message);
                TryPreserve(candidate, "corrupt", out _);
            }
        }
        return new CampaignProgress();
    }

    private static CampaignProgress Read(string path, out bool migrated)
    {
        var data = JsonSerializer.Deserialize<SaveDocument>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Empty Exodus save.");
        migrated = data.Version == 1;
        if (data.Version is not (1 or 2) || data.Hero is < 0 or > 3 || data.Completed == null || data.BestTicks == null)
            throw new InvalidDataException("Invalid Exodus save format.");
        var ids = migrated ? CampaignCatalog.LegacyRouteIds.ToArray() : data.ChapterIds;
        var expected = migrated ? CampaignCatalog.LegacyRouteIds : CampaignCatalog.RouteIds;
        if (ids == null || ids.Length != expected.Count || data.Completed.Length != ids.Length || data.BestTicks.Length != ids.Length
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || !ids.ToHashSet(StringComparer.Ordinal).SetEquals(expected)
            || data.BestTicks.Any(ticks => ticks < 0))
            throw new InvalidDataException("Invalid Exodus checkpoint records.");

        if (migrated)
        {
            var gap = false;
            foreach (var complete in data.Completed)
            {
                if (!complete) gap = true;
                else if (gap) throw new InvalidDataException("Non-contiguous legacy campaign save.");
            }
        }
        var progress = new CampaignProgress { Hero = data.Hero };
        for (var index = 0; index < ids.Length; index++)
        {
            var destination = Array.IndexOf(progress.ChapterIds, ids[index]);
            progress.Completed[destination] = data.Completed[index];
            progress.BestTicks[destination] = data.BestTicks[index];
        }
        ValidateCompletionOrder(progress);
        return progress;
    }

    private static void ValidateCompletionOrder(CampaignProgress progress)
    {
        // Inserted checkpoints legitimately interrupt old victories. A missing original
        // checkpoint still makes any later completion invalid, just as in version one.
        var blockedByOriginal = false;
        for (var index = 0; index < progress.ChapterIds.Length; index++)
        {
            if (!progress.Completed[index] && CampaignCatalog.LegacyRouteIds.Contains(progress.ChapterIds[index]))
                blockedByOriginal = true;
            else if (progress.Completed[index] && blockedByOriginal)
                throw new InvalidDataException("A completed checkpoint follows an unfinished original checkpoint.");
        }
    }

    public bool Write(CampaignProgress progress)
    {
        try
        {
            ValidateForWrite(progress);
            var preserveBackup = false;
            if (File.Exists(Path))
            {
                try
                {
                    Read(Path, out var legacy);
                    if (legacy)
                    {
                        if (!PreserveLegacyFiles()) return false;
                        preserveBackup = true;
                    }
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    // Never replace a recoverable rolling backup with a corrupt primary.
                    if (!TryPreserve(Path, "corrupt", out _)) return false;
                    preserveBackup = true;
                }
            }
            return WriteCore(progress, preserveBackup);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            Error = "SAVE FAILED - CHECK DISK";
            log("Exodus save: " + ex.Message);
            return false;
        }
    }

    private static void ValidateForWrite(CampaignProgress progress)
    {
        if (progress.Version != 2 || progress.ChapterIds == null || !progress.ChapterIds.SequenceEqual(CampaignCatalog.RouteIds)
            || progress.Completed?.Length != CampaignCatalog.ChapterCount || progress.BestTicks?.Length != CampaignCatalog.ChapterCount
            || progress.Hero is < 0 or > 3 || progress.BestTicks.Any(ticks => ticks < 0))
            throw new InvalidDataException("Invalid current campaign progress.");
        ValidateCompletionOrder(progress);
    }

    private bool WriteCore(CampaignProgress progress, bool preserveRollingBackup)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path + ".tmp", JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(Path) && !preserveRollingBackup) File.Copy(Path, Path + ".bak", true);
            File.Move(Path + ".tmp", Path, true);
            Error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "SAVE FAILED - CHECK DISK"; log("Exodus save: " + ex.Message); return false; }
    }
    private bool PreserveLegacyFiles()
    {
        foreach (var candidate in new[] { Path, Path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            if (!TryPreserve(candidate, candidate == Path ? "v1-primary" : "v1-backup", out var archived)) return false;
            MigrationArchivePath ??= archived;
        }
        return true;
    }

    private bool TryPreserve(string source, string reason, out string? archived)
    {
        archived = null;
        try
        {
            var destination = Path + "." + reason + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")
                + "." + Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(source, destination, overwrite: false);
            archived = destination;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "SAVE BACKUP FAILED"; log(ex.Message); return false; }
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException;

    public bool Archive() => !File.Exists(Path) || TryPreserve(Path, "journey", out _);
}
