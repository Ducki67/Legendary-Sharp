using Legendary_Sharp.Downloader;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal sealed class Session : IDisposable
{
    private ReleaseCatalog? _catalog;
    private UefnCatalog? _uefnCatalog;

    private Session(AppPaths paths, AppSettings settings, ChunkSource http)
    {
        Paths = paths;
        Settings = settings;
        Http = http;
    }

    public AppPaths Paths { get; }

    public AppSettings Settings { get; }

    public ChunkSource Http { get; }

    public static Session Create()
    {
        var paths = AppPaths.Create();
        var settings = AppSettings.Load(paths);
        var http = new ChunkSource(settings.EffectiveMirrors, settings.Workers, TimeSpan.FromSeconds(45));
        return new Session(paths, settings, http);
    }

    public async Task<ReleaseCatalog> CatalogAsync(bool refresh, CancellationToken cancellation)
    {
        if (_catalog is not null && !refresh) return _catalog;

        _catalog = await ReleaseCatalog
            .LoadAsync(Http, Paths, TimeSpan.FromHours(Settings.CatalogMaxAgeHours), refresh, cancellation)
            .ConfigureAwait(false);

        return _catalog;
    }

    public async Task<UefnCatalog> UefnCatalogAsync(bool refresh, CancellationToken cancellation)
    {
        if (_uefnCatalog is not null && !refresh) return _uefnCatalog;

        _uefnCatalog = await UefnCatalog
            .LoadAsync(Http, Paths, TimeSpan.FromHours(Settings.CatalogMaxAgeHours), refresh, cancellation)
            .ConfigureAwait(false);

        return _uefnCatalog;
    }

    public DownloadOptions BuildDownloadOptions(
        string installRoot,
        int? workers,
        int? cacheBudgetMiB,
        bool skipMissing = false) =>
        new()
        {
            InstallRoot = installRoot,
            Mirrors = Settings.EffectiveMirrors,
            Workers = Math.Clamp(workers ?? Settings.Workers, 1, 64),
            CacheBudgetBytes = Math.Max(64, cacheBudgetMiB ?? Settings.CacheBudgetMiB) * 1024L * 1024L,
            SkipMissing = skipMissing
        };

    public void Dispose() => Http.Dispose();
}
