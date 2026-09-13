namespace Legendary_Sharp.Fortnite;

internal static class FortniteConstants
{
    public const string AppName = "Fortnite";

    public const string ShippingExecutable = "FortniteClient-Win64-Shipping.exe";

    public const string BinariesFolder = @"FortniteGame\Binaries\Win64";

    public const string ReleaseIndexUrl =
        "https://raw.githubusercontent.com/polynite/fn-releases/master/README.md";

    public const string ReleaseManifestUrl =
        "https://raw.githubusercontent.com/polynite/fn-releases/master/manifests";

    public const string ReleaseRepositoryUrl = "https://github.com/polynite/fn-releases";

    public const string ArchiveIndexUrl =
        "https://raw.githubusercontent.com/VastBlast/FortniteManifestArchive/main/README.md";

    public const string ArchiveManifestUrl =
        "https://raw.githubusercontent.com/VastBlast/FortniteManifestArchive/main/Fortnite/Windows";

    public const string ArchiveRepositoryUrl = "https://github.com/VastBlast/FortniteManifestArchive";

    public const string UefnIndexUrl =
        "https://raw.githubusercontent.com/Mast3rGamers/UEFN-releases/main/README.md";

    public const string UefnManifestUrl =
        "https://raw.githubusercontent.com/Mast3rGamers/UEFN-releases/main/archive";

    public const string UefnRepositoryUrl = "https://github.com/Mast3rGamers/UEFN-releases";

    public const string UefnExecutable = "UnrealEditorFortnite-Win64-Shipping.exe";

    public static readonly string[] CloudMirrors =
    [
        "https://epicgames-download1.akamaized.net/Builds/Fortnite/CloudDir",
        "https://download.epicgames.com/Builds/Fortnite/CloudDir",
        "https://fastly-download.epicgames.com/Builds/Fortnite/CloudDir",
        "https://cloudflare.epicgamescdn.com/Builds/Fortnite/CloudDir"
    ];
}
