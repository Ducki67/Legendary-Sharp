using System.Text.Json;
using System.Text.Json.Serialization;
using Legendary_Sharp.Application;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Fortnite;

internal sealed class InstallRecord
{
    public const string FolderName = ".legendarysharp";
    public const string FileName = "install.json";
    public const string ManifestName = "manifest.bin";
    public const string UefnManifestName = "uefn.bin";

    [JsonPropertyName("buildVersion")] public string BuildVersion { get; set; } = string.Empty;

    [JsonPropertyName("manifestId")] public string ManifestId { get; set; } = string.Empty;

    [JsonPropertyName("appName")] public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("launchExe")] public string LaunchExe { get; set; } = string.Empty;

    [JsonPropertyName("installTags")] public List<string> InstallTags { get; set; } = [];

    [JsonPropertyName("installSize")] public long InstallSize { get; set; }

    [JsonPropertyName("completedUtc")] public DateTime? CompletedUtc { get; set; }

    [JsonPropertyName("startedUtc")] public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("uefnManifestId")] public string UefnManifestId { get; set; } = string.Empty;

    [JsonPropertyName("uefnInstalledUtc")] public DateTime? UefnInstalledUtc { get; set; }

    [JsonIgnore] public bool HasUefn => UefnInstalledUtc is not null;

    [JsonIgnore] public bool IsComplete => CompletedUtc is not null;

    public static string StateDirectory(string installRoot) => Path.Combine(installRoot, FolderName);

    public static string RecordPath(string installRoot) => Path.Combine(StateDirectory(installRoot), FileName);

    public static string ManifestPath(string installRoot) => Path.Combine(StateDirectory(installRoot), ManifestName);

    public static string UefnManifestPath(string installRoot) =>
        Path.Combine(StateDirectory(installRoot), UefnManifestName);

    public static InstallRecord? TryLoad(string installRoot)
    {
        var path = RecordPath(installRoot);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppJson.Default.InstallRecord);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string installRoot)
    {
        AtomicFile.WriteAllText(RecordPath(installRoot), JsonSerializer.Serialize(this, AppJson.Default.InstallRecord));
    }
}
