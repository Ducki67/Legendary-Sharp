using System.Text.Json;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed record TagPreset(string Key, string Name, string Description)
{
    public required Func<TagGroup, bool> Includes { get; init; }

    public List<string> Resolve(IEnumerable<TagGroup> groups) =>
        InstallTagCatalog.Flatten(groups.Where(Includes));
}

internal static class TagPresets
{
    public const string FullKey = "full";

    public static readonly TagPreset Full = new(
        FullKey,
        "Full build",
        "Everything the manifest offers, the closest you get to a complete build")
    {
        Includes = _ => true
    };

    public static readonly TagPreset PlayableHighRes = new(
        "playable-hd",
        "Battle Royale + HD",
        "Core, Battle Royale and high resolution textures, no language packs or extra modes")
    {
        Includes = group => group.Kind is TagKind.Untagged or TagKind.Core or TagKind.BattleRoyale or TagKind.HighRes
    };

    public static readonly TagPreset Modes = new(
        "modes",
        "Battle Royale + modes",
        "Core, Battle Royale and the other game modes, no language packs")
    {
        Includes = group => group.Kind is TagKind.Untagged or TagKind.Core or TagKind.BattleRoyale
                        or TagKind.HighRes or TagKind.GameMode
    };

    public static readonly TagPreset Playable = new(
        "playable",
        "Battle Royale",
        "Core plus Battle Royale, no high resolution textures or language packs")
    {
        Includes = group => group.Kind is TagKind.Untagged or TagKind.Core or TagKind.BattleRoyale
    };

    public static readonly TagPreset SaveTheWorld = new(
        "stw",
        "Save the World",
        "Core plus Save the World content")
    {
        Includes = group => group.Kind is TagKind.Untagged or TagKind.Core or TagKind.SaveTheWorld
    };

    public static readonly TagPreset Minimal = new(
        "minimal",
        "Minimal",
        "Untagged and core files only, smallest possible download")
    {
        Includes = group => group.Kind is TagKind.Untagged or TagKind.Core
    };

    public static readonly IReadOnlyList<TagPreset> All =
        [Full, Modes, PlayableHighRes, Playable, SaveTheWorld, Minimal];

    public static TagPreset? Find(string key) =>
        All.FirstOrDefault(preset => preset.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static List<string> FromSelectiveDownloadFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a selective download file with a \"data\" object.");

        var tags = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var group in data.EnumerateObject())
        {
            if (!group.Value.TryGetProperty("tags", out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var tag in list.EnumerateArray()) tags.Add(tag.GetString() ?? string.Empty);
        }

        return [.. tags];
    }

    public static List<string> Intersect(IReadOnlyList<string> requested, BuildManifest manifest)
    {
        var available = new HashSet<string>(manifest.InstallTags, StringComparer.Ordinal);
        if (manifest.HasUntaggedFiles) available.Add(string.Empty);

        return [.. requested.Where(available.Contains).Distinct(StringComparer.Ordinal)];
    }
}
