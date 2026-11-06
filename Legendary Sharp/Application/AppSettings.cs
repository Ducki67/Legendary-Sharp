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

    [JsonPropertyName("easeOff")] public bool EaseOff { get; set; } = true;

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

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0) return Path.Combine(home, "FortniteBuilds");

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
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(paths.SettingsFile), AppJson.Default.AppSettings)
                         ?? new AppSettings();

            loaded.LibraryRoots ??= [];
            loaded.Mirrors ??= [];
            return loaded;
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
        EaseOff = defaults.EaseOff;
    }

    public void Save(AppPaths paths)
    {
        AtomicFile.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, AppJson.Default.AppSettings));
    }

    public bool TryAddLibrary(string path, AppPaths paths, out string error)
    {
        error = string.Empty;

        var trimmed = path.Trim();

        if (trimmed.Length == 0 || (trimmed.Length == 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':'))
        {
            error = "Pick a folder, not a whole drive.";
            return false;
        }

        string full;
        try
        {
            full = FolderPath.Normalize(trimmed);
        }
        catch (Exception problem) when (problem is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "That is not a valid folder path.";
            return false;
        }

        if (!Directory.Exists(full))
        {
            error = "That folder does not exist.";
            return false;
        }

        var top = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(top) || Path.TrimEndingDirectorySeparator(top).Length == full.Length)
        {
            error = "Pick a folder, not a whole drive.";
            return false;
        }

        if (FolderPath.IsInside(full, paths.Root) || FolderPath.IsInside(paths.Root, full))
        {
            error = "That is where " + AppInfo.Name + " keeps its own data.";
            return false;
        }

        if (LibraryRoots.Any(root => FolderPath.Same(root, full)) || FolderPath.Same(EffectiveInstallRoot, full))
        {
            error = "That folder is already in the list.";
            return false;
        }

        LibraryRoots.Add(full);
        return true;
    }

    public IEnumerable<string> ScanRoots()
    {
        yield return EffectiveInstallRoot;
        foreach (var root in LibraryRoots) yield return root;
    }
}
