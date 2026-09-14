using System.Text.Json;
using System.Text.Json.Serialization;
using Legendary_Sharp.Downloader;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class AppSettings
{
    public const string MouseFull = "full";
    public const string MouseScroll = "scroll";
    public const string MouseOff = "off";

    [JsonPropertyName("installRoot")] public string InstallRoot { get; set; } = string.Empty;

    [JsonPropertyName("libraryRoots")] public List<string> LibraryRoots { get; set; } = [];

    [JsonPropertyName("workers")] public int Workers { get; set; } = DownloadOptions.DefaultWorkers();

    [JsonPropertyName("cacheBudgetMiB")] public int CacheBudgetMiB { get; set; } = 1024;

    [JsonPropertyName("mirrors")] public List<string> Mirrors { get; set; } = [.. FortniteConstants.CloudMirrors];

    [JsonPropertyName("preset")] public string Preset { get; set; } = TagPresets.FullKey;

    [JsonPropertyName("catalogMaxAgeHours")] public int CatalogMaxAgeHours { get; set; } = 12;

    [JsonPropertyName("offerUefn")] public bool OfferUefn { get; set; } = true;

    [JsonPropertyName("mouse")] public string Mouse { get; set; } = MouseFull;

    [JsonIgnore] public PointerMode Pointer => Mouse switch
    {
        MouseScroll => PointerMode.Scroll,
        MouseOff => PointerMode.Off,
        _ => PointerMode.Full
    };

    [JsonIgnore] public IReadOnlyList<string> EffectiveMirrors =>
        Mirrors.Count > 0 ? Mirrors : FortniteConstants.CloudMirrors;

    [JsonIgnore] public bool UsesDefaultInstallRoot => InstallRoot.Length == 0;

    [JsonIgnore] public string EffectiveInstallRoot =>
        InstallRoot.Length > 0 ? InstallRoot : DefaultInstallRoot();

    public static string NameOf(PointerMode mode) => mode switch
    {
        PointerMode.Scroll => MouseScroll,
        PointerMode.Off => MouseOff,
        _ => MouseFull
    };

    public static string DefaultInstallRoot()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.Length > 0) return Path.Combine(documents, "FortniteBuilds");

        var drives = DriveInfo.GetDrives()
            .Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed })
            .OrderByDescending(drive => drive.AvailableFreeSpace)
            .ToList();

        var root = drives.Count > 0 ? drives[0].RootDirectory.FullName : AppContext.BaseDirectory;
        return Path.Combine(root, "FortniteBuilds");
    }

    public static AppSettings Load(AppPaths paths)
    {
        if (!File.Exists(paths.SettingsFile)) return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(paths.SettingsFile), AppJson.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        InstallRoot = defaults.InstallRoot;
        LibraryRoots = defaults.LibraryRoots;
        Workers = defaults.Workers;
        CacheBudgetMiB = defaults.CacheBudgetMiB;
        Mirrors = defaults.Mirrors;
        Preset = defaults.Preset;
        CatalogMaxAgeHours = defaults.CatalogMaxAgeHours;
        OfferUefn = defaults.OfferUefn;
        Mouse = defaults.Mouse;
    }

    public void Save(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, AppJson.Default.AppSettings));
    }

    public IEnumerable<string> ScanRoots()
    {
        yield return EffectiveInstallRoot;
        foreach (var root in LibraryRoots) yield return root;
    }
}
