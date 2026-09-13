using System.Threading.Channels;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class DownloadEngine(DownloadOptions options)
{
    private readonly RateMeter _networkRate = new(TimeSpan.FromSeconds(6));
    private readonly RateMeter _diskRate = new(TimeSpan.FromSeconds(6));
    private readonly HashSet<string> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FailedFile> _failures = [];

    private long _writtenBytes;
    private int _filesDone;
    private int _chunksDone;
    private int _activeWorkers;
    private string _currentFile = string.Empty;

    public async Task<DownloadResult> RunAsync(
        DownloadPlan plan,
        ResumeLog resume,
        Action<DownloadProgress> onProgress,
        CancellationToken cancellation)
    {
        var started = DateTime.UtcNow;
        var slots = ResolveSlots(plan, options);

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var cache = new ChunkCache(plan.References, slots);
        using var source = new ChunkSource(options.Mirrors, options.Workers, options.RequestTimeout, options.Attempts);

        var channel = Channel.CreateBounded<ChunkInfo>(new BoundedChannelOptions(options.Workers)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var dispatcher = DispatchAsync(plan, cache, channel.Writer, abort.Token);
        var workers = Enumerable
            .Range(0, options.Workers)
            .Select(_ => ConsumeAsync(plan.Manifest.FeatureLevel, source, cache, channel.Reader, abort.Token))
            .ToArray();

        var reporter = ReportAsync(plan, source, cache, started, onProgress, abort.Token);

        try
        {
            await WriteAsync(plan, cache, resume, abort.Token).ConfigureAwait(false);
            await dispatcher.ConfigureAwait(false);
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch
        {
            await abort.CancelAsync().ConfigureAwait(false);
            await Quiet(dispatcher).ConfigureAwait(false);
            await Quiet(Task.WhenAll(workers)).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await abort.CancelAsync().ConfigureAwait(false);
            await Quiet(reporter).ConfigureAwait(false);
        }

        var elapsed = DateTime.UtcNow - started;
        onProgress(Snapshot(plan, source, cache, elapsed));

        return new DownloadResult(
            Volatile.Read(ref _filesDone),
            Volatile.Read(ref _chunksDone),
            source.Transferred,
            Interlocked.Read(ref _writtenBytes),
            elapsed,
            _failures);
    }

    public static int ResolveSlots(DownloadPlan plan, DownloadOptions options)
    {
        var window = ChunkWindow(plan);
        var budgeted = (int)Math.Max(options.Workers * 4L, options.CacheBudgetBytes / window);
        var required = (int)Math.Ceiling((double)plan.PeakCacheBytes / window);
        return Math.Max(budgeted, Math.Max(required, options.Workers * 2));
    }

    public static long ResolveMemoryBound(DownloadPlan plan, DownloadOptions options) =>
        (long)ResolveSlots(plan, options) * ChunkWindow(plan);

    private static int ChunkWindow(DownloadPlan plan)
    {
        var window = plan.ChunkOrder.Count == 0 ? 0 : (int)plan.Manifest.Chunk(plan.ChunkOrder[0]).WindowSize;
        return window > 0 ? window : 1024 * 1024;
    }

    private static async Task DispatchAsync(
        DownloadPlan plan,
        ChunkCache cache,
        ChannelWriter<ChunkInfo> writer,
        CancellationToken cancellation)
    {
        try
        {
            foreach (var guid in plan.ChunkOrder)
            {
                if (!await cache.ReserveAsync(guid, cancellation).ConfigureAwait(false)) continue;
                await writer.WriteAsync(plan.Manifest.Chunk(guid), cancellation).ConfigureAwait(false);
            }

            writer.Complete();
        }
        catch (Exception error)
        {
            writer.TryComplete(error);
            throw;
        }
    }

    private async Task ConsumeAsync(
        int featureLevel,
        ChunkSource source,
        ChunkCache cache,
        ChannelReader<ChunkInfo> reader,
        CancellationToken cancellation)
    {
        await foreach (var chunk in reader.ReadAllAsync(cancellation).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _activeWorkers);

            try
            {
                var data = await source.FetchAsync(chunk, featureLevel, cancellation).ConfigureAwait(false);
                cache.Publish(chunk.Guid, data);
                Interlocked.Increment(ref _chunksDone);
            }
            catch (Exception error)
            {
                cache.Abort(chunk.Guid, error);
                if (!options.SkipMissing || error is OperationCanceledException) throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
            }
        }
    }

    private async Task WriteAsync(DownloadPlan plan, ChunkCache cache, ResumeLog resume, CancellationToken cancellation)
    {
        foreach (var file in plan.Files)
        {
            cancellation.ThrowIfCancellationRequested();

            var target = Path.Combine(options.InstallRoot, file.ToNativePath());
            EnsureDirectory(Path.GetDirectoryName(target));
            ClearReadOnly(target);

            var failure = await WriteFileAsync(file, target, cache, cancellation).ConfigureAwait(false);

            if (failure is not null)
            {
                _failures.Add(new FailedFile(file.FileName, failure.Message));
                Delete(target);
                continue;
            }

            ApplyAttributes(target, file);
            resume.MarkComplete(file);
            Interlocked.Increment(ref _filesDone);
        }
    }

    private async Task<Exception?> WriteFileAsync(
        FileEntry file,
        string target,
        ChunkCache cache,
        CancellationToken cancellation)
    {
        var index = 0;

        try
        {
            await using var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                1 << 16, FileOptions.SequentialScan);

            if (file.Size > 0) stream.SetLength(file.Size);
            Volatile.Write(ref _currentFile, file.FileName);

            for (; index < file.Parts.Length; index++)
            {
                var part = file.Parts[index];
                var data = await cache.WaitAsync(part.Guid).ConfigureAwait(false);

                if (part.Offset + (long)part.Size > data.Length)
                    throw new InvalidDataException(
                        $"Chunk {part.Guid.ToHex()} is smaller than \"{file.FileName}\" expects.");

                await stream.WriteAsync(data.AsMemory((int)part.Offset, (int)part.Size), cancellation)
                    .ConfigureAwait(false);

                cache.Release(part.Guid);
                Interlocked.Add(ref _writtenBytes, part.Size);
            }

            return null;
        }
        catch (Exception error) when (options.SkipMissing && error is not OperationCanceledException)
        {
            for (var remaining = index; remaining < file.Parts.Length; remaining++)
                cache.Release(file.Parts[remaining].Guid);

            return error;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private async Task ReportAsync(
        DownloadPlan plan,
        ChunkSource source,
        ChunkCache cache,
        DateTime started,
        Action<DownloadProgress> onProgress,
        CancellationToken cancellation)
    {
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(120));

        try
        {
            while (await ticker.WaitForNextTickAsync(cancellation).ConfigureAwait(false))
                onProgress(Snapshot(plan, source, cache, DateTime.UtcNow - started));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private DownloadProgress Snapshot(DownloadPlan plan, ChunkSource source, ChunkCache cache, TimeSpan elapsed)
    {
        var now = DateTime.UtcNow;
        var downloaded = source.Transferred;
        var written = Interlocked.Read(ref _writtenBytes);

        _networkRate.Record(downloaded, now);
        _diskRate.Record(written, now);

        var rate = _networkRate.PerSecond;
        var remaining = plan.DownloadSize - downloaded;
        var eta = rate > 1024d && remaining > 0
            ? TimeSpan.FromSeconds(remaining / rate)
            : TimeSpan.Zero;

        return new DownloadProgress(
            downloaded,
            plan.DownloadSize,
            written,
            plan.WriteSize,
            Volatile.Read(ref _filesDone),
            plan.Files.Count,
            Volatile.Read(ref _chunksDone),
            plan.ChunkCount,
            Volatile.Read(ref _activeWorkers),
            cache.Resident,
            rate,
            _diskRate.PerSecond,
            elapsed,
            eta,
            Volatile.Read(ref _currentFile));
    }

    private void EnsureDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        if (!_createdDirectories.Add(directory)) return;
        Directory.CreateDirectory(directory);
    }

    private static void ClearReadOnly(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.IsReadOnly) info.IsReadOnly = false;
    }

    private static void ApplyAttributes(string path, FileEntry file)
    {
        if (!file.IsReadOnly) return;

        try
        {
            new FileInfo(path).IsReadOnly = true;
        }
        catch (IOException)
        {
        }
    }

    private static async Task Quiet(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

internal readonly record struct FailedFile(string FileName, string Reason);

internal readonly record struct DownloadResult(
    int Files,
    int Chunks,
    long TransferredBytes,
    long WrittenBytes,
    TimeSpan Elapsed,
    IReadOnlyList<FailedFile> Failures);
