using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Legendary_Sharp.Application;

internal sealed record UpdateRelease(string Version, string FileName, string Url, string Sha256);

internal static class Updater
{
    private const string LatestUrl = "https://api.github.com/repos/Ducki67/Legendary-Sharp/releases/latest";
    private const string DigestPrefix = "sha256:";

    public static string InstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LegendarySharp");

    public static bool CanUpdate =>
        OperatingSystem.IsWindows() &&
        Environment.ProcessPath is { } path &&
        Path.GetFileNameWithoutExtension(path).StartsWith("LegendarySharp", StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled =>
        CanUpdate &&
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath!)),
            Path.GetFullPath(InstallFolder), StringComparison.OrdinalIgnoreCase);

    public static async Task<UpdateRelease?> FindAsync(Session session, CancellationToken cancellation)
    {
        var payload = await session.Http.GetAsync(LatestUrl, cancellation).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);

        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? string.Empty;
        if (!UpdateCheck.IsNewer(version)) return null;

        var wanted = $"LegendarySharp-v{version}{(IsInstalled ? ".msi" : ".exe")}";

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != wanted) continue;

            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? string.Empty : string.Empty;
            if (!digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GitHub has no checksum for {wanted}, so it can't be checked. " +
                                               $"Get it from {AppInfo.Repository}/releases instead.");

            return new UpdateRelease(version, wanted,
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                digest[DigestPrefix.Length..]);
        }

        throw new InvalidDataException($"v{version} has no {wanted}. Get it from {AppInfo.Repository}/releases instead.");
    }

    public static async Task<string> DownloadAsync(Session session, UpdateRelease release, CancellationToken cancellation)
    {
        var data = await session.Http.GetAsync(release.Url, cancellation).ConfigureAwait(false);

        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{release.FileName} did not match GitHub's checksum, so it was not used.");

        var folder = UpdatesFolder(session.Paths);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, release.FileName);
        await File.WriteAllBytesAsync(path, data, cancellation).ConfigureAwait(false);
        return path;
    }

    public static void Install(string file)
    {
        if (IsInstalled)
        {
            var start = new ProcessStartInfo("msiexec") { UseShellExecute = false };
            start.ArgumentList.Add("/i");
            start.ArgumentList.Add(file);
            start.ArgumentList.Add("/passive");
            Process.Start(start);
            return;
        }

        var current = Environment.ProcessPath!;
        var old = current + ".old";
        File.Move(current, old, overwrite: true);

        try
        {
            File.Copy(file, current);
        }
        catch
        {
            File.Move(old, current, overwrite: true);
            throw;
        }
    }

    public static void CleanUp(AppPaths paths)
    {
        try
        {
            if (Environment.ProcessPath is { } path && File.Exists(path + ".old")) File.Delete(path + ".old");
            if (!Directory.Exists(UpdatesFolder(paths))) return;

            foreach (var file in Directory.EnumerateFiles(UpdatesFolder(paths)))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1))
                    File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string UpdatesFolder(AppPaths paths) => Path.Combine(paths.Cache, "updates");
}
