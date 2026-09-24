namespace Legendary_Sharp.Application;

internal sealed class AppPaths
{
    private AppPaths(string root)
    {
        Root = root;
        Cache = Path.Combine(root, "cache");
        Manifests = Path.Combine(Cache, "manifests");
        Queue = Path.Combine(root, "queue");
        SettingsFile = Path.Combine(root, "settings.json");
    }

    public string Root { get; }

    public string Cache { get; }

    public string Manifests { get; }

    public string Queue { get; }

    public string SettingsFile { get; }

    public static AppPaths Create()
    {
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseFolder)) baseFolder = AppContext.BaseDirectory;

        var paths = new AppPaths(Path.Combine(baseFolder, "LegendarySharp"));
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.Manifests);
        return paths;
    }
}
