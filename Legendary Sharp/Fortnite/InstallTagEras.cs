using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Fortnite;

internal sealed record TagEra(string Key, string Name, string Range, IReadOnlyList<string> Tags);

internal static class InstallTagEras
{
    public static readonly TagEra Chunk = new("chunk", "Chunk era", "13.40 through 18.30",
    [
        "", "chunk0", "chunk0optional", "chunk1", "chunk2", "chunk2optional", "chunk5", "chunk5optional",
        "chunk7", "chunk7optional", "chunk8", "chunk8optional", "chunk9", "chunk9optional",
        "chunk10", "chunk10optional", "chunk11", "chunk11optional", "chunk1000"
    ]);

    public static readonly TagEra Named = new("named", "Named era", "21.10 through 33.x",
    [
        "", "br", "br_highres", "br_ondemand", "br_ondemandoptional", "br_sm6", "core", "core_highres",
        "core_ondemand", "core_ondemandoptional", "loc_de", "loc_de_highres", "loc_fr", "loc_fr_highres",
        "loc_pl", "loc_pl_highres", "loc_ru", "loc_ru_highres", "loc_zh-CN", "loc_zh-CN_highres",
        "stw", "stw_highres"
    ]);

    public static readonly TagEra Gameplay = new("gfp", "Gameplay era", "34.10 and newer",
    [
        "", "CosmeticPreInstall", "CosmeticPreInstallOptional", "DefaultGameplayChunk",
        "DefaultGameplayChunkOptional", "Encrypted", "EncryptedOptional", "FNOne", "FNOneOptional",
        "FatCosmo", "FatCosmoOptional", "FortniteBR", "FortniteBROnDemand", "FortniteBROnDemandOptional",
        "FortniteBROptional", "FortniteCatchAll", "FortniteCatchAllOptional", "FrontEnd", "FrontEndOptional",
        "GFP_BaseInstallRoot", "GFP_BaseInstallRootOptional", "GFP_BRCosmetics", "GFP_BRCosmeticsOptional",
        "GFP_BRCosmeticsInstallOnDemand", "GFP_BRCosmeticsInstallOnDemandFat", "GFP_Alpine", "GFP_BRRoot",
        "GFP_BRRootOptional", "GFP_BlitzRoot", "GFP_BlitzRootOptional", "GFP_Borealis", "GFP_CreativeRoot",
        "GFP_CreativeRootOptional", "GFP_DM_404_S2_B", "GFP_DelMarRoot", "GFP_DelMarRootOptional",
        "GFP_GoldRush", "GFP_JunoRoot", "GFP_JunoRootOptional", "GFP_SaveTheWorldRoot",
        "GFP_SaveTheWorldRootOptional", "GFP_Sparks", "GFP_SparksOptional", "GFP_StrideMiceRoot",
        "GFP_StrideMiceRootOptional", "Globals", "Langde", "LangdeOptional", "Langes419", "Langes419Optional",
        "LangesES", "LangesESOptional", "Langfr", "LangfrOptional", "Langit", "LangitOptional", "Langpl",
        "LangplOptional", "Langru", "LangruOptional", "LangzhCN", "LangzhCNOptional", "OfferCatalog",
        "OfferCatalogOptional", "Startup", "StartupOptional", "UnusedOldEarlyStartupPatcherChunk"
    ]);

    public static readonly IReadOnlyList<TagEra> All = [Chunk, Named, Gameplay];

    public static TagEra? Find(string key) =>
        All.FirstOrDefault(era => era.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static TagEra? Detect(BuildManifest manifest)
    {
        if (manifest.InstallTags.Count == 0) return null;

        var present = new HashSet<string>(manifest.InstallTags, StringComparer.OrdinalIgnoreCase);

        return All
            .Select(era => (Era: era, Overlap: era.Tags.Count(tag => tag.Length > 0 && present.Contains(tag))))
            .Where(item => item.Overlap > 0)
            .OrderByDescending(item => item.Overlap)
            .Select(item => item.Era)
            .FirstOrDefault();
    }
}
