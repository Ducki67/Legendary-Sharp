using System.Text.Json;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Application;

internal static class UpdateCheck
{
    private const string LatestUrl = "https://api.github.com/repos/Ducki67/Legendary-Sharp/releases/latest";

    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static Task<string?> StartAsync(Session session) => Task.Run(() => FindAsync(session));

    private static async Task<string?> FindAsync(Session session)
    {
        try
        {
            var cache = Path.Combine(session.Paths.Cache, "latest-version.txt");
            var info = new FileInfo(cache);

            string latest;

            if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaximumAge)
            {
                latest = (await File.ReadAllTextAsync(cache).ConfigureAwait(false)).Trim();
            }
            else
            {
                using var timeout = new CancellationTokenSource(Timeout);
                var payload = await session.Http.GetAsync(LatestUrl, timeout.Token).ConfigureAwait(false);

                using var document = JsonDocument.Parse(payload);
                latest = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
                AtomicFile.WriteAllText(cache, latest);
            }

            return IsNewer(latest) ? latest : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsNewer(string latest) =>
        Version.TryParse(latest, out var remote) &&
        Version.TryParse(AppInfo.Version, out var local) &&
        remote > local;
}
