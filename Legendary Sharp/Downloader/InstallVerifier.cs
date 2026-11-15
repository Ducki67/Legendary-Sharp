using System.Security.Cryptography;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal static class InstallVerifier
{
    private const int Readers = 4;

    public static async Task<VerifyReport> VerifyAsync(
        IReadOnlyList<FileEntry> files,
        string installRoot,
        Action<VerifyProgress> onProgress,
        CancellationToken cancellation)
    {
        var states = new FileState[files.Count];
        var total = files.Sum(file => file.Size);
        var started = DateTime.UtcNow;
        var gate = new Lock();
        var done = 0;
        long verifiedBytes = 0;
        var next = -1;

        async Task ReadAsync()
        {
            var buffer = new byte[1 << 20];
            int index;

            while ((index = Interlocked.Increment(ref next)) < files.Count)
            {
                cancellation.ThrowIfCancellationRequested();

                var file = files[index];
                var (state, bytes) = await CheckAsync(file, installRoot, buffer, cancellation).ConfigureAwait(false);
                states[index] = state;

                lock (gate)
                {
                    done++;
                    verifiedBytes += bytes;
                    onProgress(new VerifyProgress(done, files.Count, verifiedBytes, total,
                        DateTime.UtcNow - started, file.FileName));
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(Readers, Math.Max(1, files.Count)))
            .Select(_ => Task.Run(ReadAsync, cancellation))).ConfigureAwait(false);

        var missing = new List<FileEntry>();
        var damaged = new List<FileEntry>();

        for (var index = 0; index < files.Count; index++)
        {
            if (states[index] == FileState.Missing) missing.Add(files[index]);
            else if (states[index] == FileState.Damaged) damaged.Add(files[index]);
        }

        return new VerifyReport(files.Count - missing.Count - damaged.Count, missing, damaged);
    }

    private static async Task<(FileState State, long Bytes)> CheckAsync(
        FileEntry file,
        string installRoot,
        byte[] buffer,
        CancellationToken cancellation)
    {
        var path = Path.Combine(installRoot, file.ToNativePath());
        var info = new FileInfo(path);

        if (!info.Exists) return (FileState.Missing, 0);
        if (info.Length != file.Size) return (FileState.Damaged, info.Length);

        return await MatchesAsync(path, file.ShaHash, buffer, cancellation).ConfigureAwait(false)
            ? (FileState.Healthy, file.Size)
            : (FileState.Damaged, file.Size);
    }

    private enum FileState
    {
        Healthy,
        Missing,
        Damaged
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
