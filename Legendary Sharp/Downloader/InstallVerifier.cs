using System.Security.Cryptography;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal static class InstallVerifier
{
    public static async Task<VerifyReport> VerifyAsync(
        IReadOnlyList<FileEntry> files,
        string installRoot,
        Action<VerifyProgress> onProgress,
        CancellationToken cancellation)
    {
        var missing = new List<FileEntry>();
        var damaged = new List<FileEntry>();
        var healthy = 0;
        long verifiedBytes = 0;
        var total = files.Sum(file => file.Size);
        var started = DateTime.UtcNow;
        var buffer = new byte[1 << 20];

        for (var index = 0; index < files.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();

            var file = files[index];
            var path = Path.Combine(installRoot, file.ToNativePath());
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                missing.Add(file);
            }
            else if (info.Length != file.Size)
            {
                damaged.Add(file);
                verifiedBytes += info.Length;
            }
            else if (await MatchesAsync(path, file.ShaHash, buffer, cancellation).ConfigureAwait(false))
            {
                healthy++;
                verifiedBytes += file.Size;
            }
            else
            {
                damaged.Add(file);
                verifiedBytes += file.Size;
            }

            onProgress(new VerifyProgress(index + 1, files.Count, verifiedBytes, total,
                DateTime.UtcNow - started, file.FileName));
        }

        return new VerifyReport(healthy, missing, damaged);
    }

    private static async Task<bool> MatchesAsync(
        string path,
        byte[] expected,
        byte[] buffer,
        CancellationToken cancellation)
    {
        if (expected.Length != 20) return true;

        using var sha = SHA1.Create();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            buffer.Length, FileOptions.SequentialScan);

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (read == 0) break;
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return sha.Hash is { } hash && hash.AsSpan().SequenceEqual(expected);
    }
}

internal readonly record struct VerifyProgress(
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    long BytesTotal,
    TimeSpan Elapsed,
    string CurrentFile);

internal sealed record VerifyReport(int Healthy, IReadOnlyList<FileEntry> Missing, IReadOnlyList<FileEntry> Damaged)
{
    public int Broken => Missing.Count + Damaged.Count;

    public bool IsHealthy => Broken == 0;

    public IReadOnlyList<FileEntry> NeedsRepair => [.. Missing.Concat(Damaged)];
}
