using System.Runtime.InteropServices;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal enum TagKind
{
    Untagged,
    Core,
    BattleRoyale,
    SaveTheWorld,
    HighRes,
    OnDemand,
    GameMode,
    Language,
    Other
}

internal sealed record TagGroup(
    IReadOnlyList<string> Tags,
    TagKind Kind,
    string Description,
    int Files,
    long InstallSize)
{
    public string Primary => Tags[0];

    public string Display => Primary.Length == 0 ? "(untagged)" : string.Join(", ", Tags);
}

internal static class InstallTagCatalog
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["core"] = "Core game files",
        ["core_highres"] = "Core high resolution textures",
        ["core_ondemand"] = "Core streamed content",
        ["core_ondemandoptional"] = "Core optional streamed content",
        ["br"] = "Battle Royale",
        ["br_highres"] = "Battle Royale high resolution textures",
        ["br_ondemand"] = "Battle Royale streamed content",
        ["br_ondemandoptional"] = "Battle Royale optional streamed content",
        ["br_sm6"] = "Shader Model 6 shaders for DirectX 12",
        ["stw"] = "Save the World",
        ["stw_highres"] = "Save the World high resolution textures",
        ["chunk0"] = "Core game files",
        ["chunk1"] = "Additional core content",
        ["chunk2"] = "Language pack - Deutsch",
        ["chunk5"] = "Language pack - Francais",
        ["chunk7"] = "Language pack - Polski",
        ["chunk8"] = "Language pack - Russkiy",
        ["chunk9"] = "Language pack - Chinese",
        ["chunk10"] = "Battle Royale",
        ["chunk10optional"] = "High resolution textures",
        ["chunk10sm6"] = "Shader Model 6 shaders for DirectX 12",
        ["chunk11"] = "Save the World",
        ["chunk11optional"] = "Save the World high resolution textures"
    };

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ar"] = "Arabic",
        ["de"] = "Deutsch",
        ["en"] = "English",
        ["es"] = "Espanol",
        ["es-419"] = "Espanol (Latinoamerica)",
        ["fr"] = "Francais",
        ["it"] = "Italiano",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
        ["pl"] = "Polski",
        ["pt-BR"] = "Portugues (Brasil)",
        ["ru"] = "Russkiy",
        ["tr"] = "Turkce",
        ["zh-CN"] = "Chinese (Simplified)",
        ["zh-Hant"] = "Chinese (Traditional)",
        ["es419"] = "Espanol (Latinoamerica)",
        ["esES"] = "Espanol (Espana)",
        ["zhCN"] = "Chinese (Simplified)",
        ["ptBR"] = "Portugues (Brasil)"
    };

    private static readonly Dictionary<string, string> GameModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GFP_JunoRoot"] = "LEGO Fortnite",
        ["GFP_DelMarRoot"] = "Rocket Racing",
        ["GFP_Sparks"] = "Fortnite Festival",
        ["GFP_CreativeRoot"] = "Creative and islands",
        ["GFP_BlitzRoot"] = "Blitz Royale",
        ["GFP_Alpine"] = "Additional experience content",
        ["GFP_Borealis"] = "Additional experience content",
        ["GFP_GoldRush"] = "Additional experience content",
        ["GFP_StrideMiceRoot"] = "Additional experience content",
        ["GFP_DM_404_S2_B"] = "Additional experience content"
    };

    private static readonly Dictionary<string, string> Gameplay = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CosmeticPreInstall"] = "Preinstalled cosmetics",
        ["DefaultGameplayChunk"] = "Default gameplay data",
        ["Encrypted"] = "Encrypted gameplay data",
        ["FNOne"] = "Core gameplay data",
        ["FatCosmo"] = "Large cosmetic data",
        ["FortniteBR"] = "Battle Royale",
        ["FortniteCatchAll"] = "Remaining game data",
        ["FrontEnd"] = "Lobby and front end",
        ["Globals"] = "Global shared data",
        ["OfferCatalog"] = "Item shop catalog",
        ["Startup"] = "Startup data",
        ["GFP_BaseInstallRoot"] = "Base install data",
        ["GFP_BRRoot"] = "Battle Royale data",
        ["GFP_BRCosmetics"] = "Battle Royale cosmetics",
        ["GFP_SaveTheWorldRoot"] = "Save the World",
        ["UnusedOldEarlyStartupPatcherChunk"] = "Unused legacy patcher data"
    };

    public static TagKind Classify(string tag)
    {
        if (tag.Length == 0) return TagKind.Untagged;

        var value = tag.ToLowerInvariant();

        if (value.StartsWith("loc_", StringComparison.Ordinal) ||
            value.StartsWith("lang", StringComparison.Ordinal)) return TagKind.Language;

        if (value.Contains("ondemand", StringComparison.Ordinal)) return TagKind.OnDemand;

        if (GameModes.ContainsKey(Base(tag))) return TagKind.GameMode;

        if (value.Contains("highres", StringComparison.Ordinal) ||
            value.EndsWith("optional", StringComparison.Ordinal)) return TagKind.HighRes;

        if (value is "chunk2" or "chunk5" or "chunk7" or "chunk8" or "chunk9") return TagKind.Language;

        if (value.StartsWith("stw", StringComparison.Ordinal) || value == "chunk11" ||
            value.StartsWith("gfp_savetheworld", StringComparison.Ordinal)) return TagKind.SaveTheWorld;

        if (value.StartsWith("br", StringComparison.Ordinal) || value is "chunk10" or "chunk10sm6" ||
            value.StartsWith("fortnitebr", StringComparison.Ordinal) ||
            value.StartsWith("gfp_br", StringComparison.Ordinal) ||
            value is "cosmeticpreinstall" or "fatcosmo") return TagKind.BattleRoyale;

        if (value.StartsWith("core", StringComparison.Ordinal) || value is "chunk0" or "chunk1" ||
            Gameplay.ContainsKey(tag)) return TagKind.Core;

        return TagKind.Other;
    }

    public static string Describe(string tag)
    {
        if (tag.Length == 0) return "Shared files with no tag";
        if (Known.TryGetValue(tag, out var known)) return known;

        var root = Base(tag);
        var optional = tag.EndsWith("Optional", StringComparison.Ordinal);
        var streamed = !root.Equals(Trim(tag), StringComparison.Ordinal);

        if (GameModes.TryGetValue(root, out var mode)) return Suffix(mode, optional, streamed);
        if (Gameplay.TryGetValue(root, out var gameplay)) return Suffix(gameplay, optional, streamed);

        if (tag.StartsWith("loc_", StringComparison.OrdinalIgnoreCase))
        {
            var body = tag[4..];
            var highRes = body.EndsWith("_highres", StringComparison.OrdinalIgnoreCase);
            if (highRes) body = body[..^8];

            var name = Languages.TryGetValue(body, out var language) ? language : body;
            return highRes ? $"Language pack high resolution - {name}" : $"Language pack - {name}";
        }

        if (tag.StartsWith("Lang", StringComparison.Ordinal) && tag.Length > 4)
        {
            var body = root[4..];
            var name = Languages.TryGetValue(body, out var language) ? language : body;
            return Suffix($"Language pack - {name}", optional, streamed);
        }

        if (root.StartsWith("GFP_", StringComparison.OrdinalIgnoreCase))
            return Suffix("Game feature content - " + root[4..], optional, streamed);

        return optional ? "Optional content" : "Additional content";
    }

    private static string Trim(string tag) =>
        tag.EndsWith("Optional", StringComparison.Ordinal) && tag.Length > 8 ? tag[..^8] : tag;

    private static string Base(string tag)
    {
        var value = Trim(tag);

        foreach (var marker in (string[])["InstallOnDemandFat", "InstallOnDemand", "OnDemand"])
            if (value.EndsWith(marker, StringComparison.Ordinal) && value.Length > marker.Length)
                return value[..^marker.Length];

        return value;
    }

    private static string Suffix(string text, bool optional, bool streamed)
    {
        if (streamed) text += " streamed";
        return optional ? text + " (optional)" : text;
    }

    public static IReadOnlyList<TagGroup> Summarise(BuildManifest manifest)
    {
        var members = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var index = 0; index < manifest.Files.Count; index++)
        {
            var file = manifest.Files[index];

            if (file.InstallTags.Length == 0)
            {
                Member(members, string.Empty).Add(index);
                continue;
            }

            foreach (var tag in file.InstallTags) Member(members, tag).Add(index);
        }

        var grouped = new Dictionary<int[], List<string>>(new IndexSetComparer());

        foreach (var (tag, indices) in members)
        {
            var key = indices.ToArray();
            Array.Sort(key);

            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(grouped, key, out var existed);
            if (!existed) list = [];
            list!.Add(tag);
        }

        return
        [
            .. grouped
                .Select(entry => Create(manifest, entry.Key, entry.Value))
                .OrderBy(group => group.Kind)
                .ThenBy(group => group.Primary, StringComparer.Ordinal)
        ];
    }

    public static long SizeOf(BuildManifest manifest, IReadOnlyList<string> tags) =>
        DownloadPlanner.SelectFiles(manifest, tags).Sum(file => file.Size);

    public static List<string> Flatten(IEnumerable<TagGroup> groups) => [.. groups.SelectMany(group => group.Tags)];

    private static TagGroup Create(BuildManifest manifest, int[] indices, List<string> tags)
    {
        tags.Sort(CompareTags);

        return new TagGroup(
            tags,
            Classify(tags[0]),
            Describe(tags[0]),
            indices.Length,
            indices.Sum(index => manifest.Files[index].Size));
    }

    private static int CompareTags(string left, string right)
    {
        var leftChunk = left.StartsWith("chunk", StringComparison.OrdinalIgnoreCase);
        var rightChunk = right.StartsWith("chunk", StringComparison.OrdinalIgnoreCase);
        if (leftChunk != rightChunk) return leftChunk ? 1 : -1;
        return string.CompareOrdinal(left, right);
    }

    private static List<int> Member(Dictionary<string, List<int>> members, string tag)
    {
        if (members.TryGetValue(tag, out var list)) return list;
        list = [];
        members[tag] = list;
        return list;
    }

    private sealed class IndexSetComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? left, int[]? right) =>
            left is not null && right is not null && left.AsSpan().SequenceEqual(right);

        public int GetHashCode(int[] value)
        {
            var hash = new HashCode();
            hash.AddBytes(MemoryMarshal.AsBytes(value.AsSpan()));
            return hash.ToHashCode();
        }
    }
}
