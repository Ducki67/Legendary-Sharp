using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class DownloadEngine(DownloadOptions options)
{
    private const long CheckpointBytes = 128L << 20;
    private const int Writers = 8;

    private readonly RateMeter _networkRate = new(TimeSpan.FromSeconds(6));
    private readonly RateMeter _diskRate = new(TimeSpan.FromSeconds(6));
    private readonly RateMeter _workRate = new(TimeSpan.FromSeconds(10));
    private readonly HashSet<string> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FailedFile> _failures = [];
    private readonly Lock _gate = new();

    private long _writtenBytes;
    private int _filesDone;
    private int _chunksDone;
    private int _activeWorkers;
    private string _currentFile = string.Empty;
    private Throttle _throttle = null!;

    public async Task<DownloadResult> RunAsync(
        DownloadPlan plan,
        ResumeLog resume,
        Action<DownloadProgress> onProgress,
        CancellationToken cancellation,
        Throttle? throttle = null)
    {
        var started = DateTime.UtcNow;
        _throttle = throttle ?? new Throttle(options.Workers);
        var slots = ResolveSlots(plan, options);

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var cache = new ChunkCache(plan.References, slots);
        using var source = new ChunkSource(options.Mirrors, options.Workers, options.StallTimeout, options.Attempts);

        var channel = Channel.CreateBounded<ChunkInfo>(new BoundedChannelOptions(options.Workers)
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var dispatcher = DispatchAsync(plan, cache, channel.Writer, abort.Token);
        var workers = Enumerable
            .Range(0, options.Workers)
            .Select(_ => ConsumeAsync(plan.Manifest, source, cache, channel.Reader, abort.Token))
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
        BuildManifest manifest,
        ChunkSource source,
        ChunkCache cache,
        ChannelReader<ChunkInfo> reader,
        CancellationToken cancellation)
    {
        await foreach (var chunk in reader.ReadAllAsync(cancellation).ConfigureAwait(false))
        {
            try
            {
                await _throttle.WaitAsync(cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException error)
            {
                cache.Abort(chunk.Guid, error);
                throw;
            }

            Interlocked.Increment(ref _activeWorkers);

            try
            {
                var data = await source.FetchAsync(chunk, manifest, cancellation).ConfigureAwait(false);
                cache.Publish(chunk.Guid, data);
                Interlocked.Increment(ref _chunksDone);
            }
            catch (Exception error)
            {
                cache.Abort(chunk.Guid, error);
                if (!Skippable(error)) throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
                _throttle.Release();
            }
        }
    }

    private async Task WriteAsync(DownloadPlan plan, ChunkCache cache, ResumeLog resume, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var turns = LargeFileTurns(plan);
        var next = -1;

        var writers = Enumerable.Range(0, Math.Min(Writers, Math.Max(1, plan.Files.Count)))
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    int index;
                    while ((index = Interlocked.Increment(ref next)) < plan.Files.Count)
                        await WriteInTurnAsync(index, turns, plan, cache, resume, stop.Token).ConfigureAwait(false);
                }
                catch
                {
                    await stop.CancelAsync().ConfigureAwait(false);
                    throw;
                }
            }, CancellationToken.None))
            .ToArray();

        try
        {
            await Task.WhenAll(writers).ConfigureAwait(false);
        }
        catch
        {
            var cause = writers
                .Select(writer => writer.Exception?.InnerException)
                .FirstOrDefault(error => error is not null and not OperationCanceledException);

            if (cause is not null && !cancellation.IsCancellationRequested) ExceptionDispatchInfo.Throw(cause);
            throw;
        }
    }

    private static TaskCompletionSource?[] LargeFileTurns(DownloadPlan plan)
    {
        var turns = new TaskCompletionSource?[plan.Files.Count];

        for (var index = 0; index < plan.Files.Count; index++)
            if (IsLarge(plan.Files[index], plan))
                turns[index] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return turns;
    }

    private static bool IsLarge(FileEntry file, DownloadPlan plan) =>
        file.Size > CheckpointBytes || plan.Partial?.FileName == file.FileName;

    private async Task WriteInTurnAsync(
        int index,
        TaskCompletionSource?[] turns,
        DownloadPlan plan,
        ChunkCache cache,
        ResumeLog resume,
        CancellationToken cancellation)
    {
        var turn = turns[index];
        if (turn is null)
        {
            await WriteEntryAsync(plan.Files[index], plan, cache, resume, cancellation).ConfigureAwait(false);
            return;
        }

        try
        {
            for (var earlier = index - 1; earlier >= 0; earlier--)
            {
                if (turns[earlier] is not { } previous) continue;
                await previous.Task.WaitAsync(cancellation).ConfigureAwait(false);
                break;
            }

            await WriteEntryAsync(plan.Files[index], plan, cache, resume, cancellation).ConfigureAwait(false);
        }
        finally
        {
            turn.TrySetResult();
        }
    }

    private async Task WriteEntryAsync(
        FileEntry file,
        DownloadPlan plan,
        ChunkCache cache,
        ResumeLog resume,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var target = Path.Combine(options.InstallRoot, file.ToNativePath());
        EnsureDirectory(Path.GetDirectoryName(target));
        ClearReadOnly(target);

        var failure = await WriteFileAsync(file, target, plan, cache, resume, cancellation).ConfigureAwait(false);

        if (failure is not null)
        {
            lock (_gate) _failures.Add(new FailedFile(file.FileName, failure.Message, failure is ChunkMissingException));
            Delete(target);
            return;
        }

        ApplyAttributes(target, file);
        resume.MarkComplete(file);
        if (IsLarge(file, plan)) resume.ClearPartial();
        Interlocked.Increment(ref _filesDone);
    }

    private async Task<Exception?> WriteFileAsync(
        FileEntry file,
        string target,
        DownloadPlan plan,
        ChunkCache cache,
        ResumeLog resume,
        CancellationToken cancellation)
    {
        var partial = plan.Partial is { } saved && saved.FileName == file.FileName ? saved : null;
        var large = IsLarge(file, plan);
        var index = partial?.Parts ?? 0;
        var doneParts = index;
        var bytes = partial?.Bytes ?? 0;
        var sinceCheckpoint = 0L;

        try
        {
            if (partial is null && !File.Exists(target)) resume.MarkCreated(file);

            await using var stream = await OpenTargetAsync(
                target, file.FileName, partial is null ? FileMode.Create : FileMode.OpenOrCreate, cancellation)
                .ConfigureAwait(false);

            if (file.Size > 0) stream.SetLength(file.Size);
            stream.Position = bytes;
            Volatile.Write(ref _currentFile, file.FileName);

            try
            {
                for (; index < file.Parts.Length; index++)
                {
                    var part = file.Parts[index];
                    var data = await cache.WaitAsync(part.Guid).WaitAsync(cancellation).ConfigureAwait(false);

                    if (part.Offset + (long)part.Size > data.Length)
                        throw new InvalidDataException(
                            $"Chunk {part.Guid.ToHex()} is smaller than \"{file.FileName}\" expects.");

                    await stream.WriteAsync(data.AsMemory((int)part.Offset, (int)part.Size), cancellation)
                        .ConfigureAwait(false);

                    cache.Release(part.Guid);
                    Interlocked.Add(ref _writtenBytes, part.Size);
                    bytes += part.Size;
                    doneParts = index + 1;
                    sinceCheckpoint += part.Size;

                    if (!large || sinceCheckpoint < CheckpointBytes || index + 1 >= file.Parts.Length) continue;

                    stream.Flush(true);
                    resume.SavePartial(file, doneParts, bytes);
                    sinceCheckpoint = 0;
                }
            }
            catch (Exception error) when (!Skippable(error))
            {
                try
                {
                    stream.Flush(true);
                    if (large && bytes > 0) resume.SavePartial(file, doneParts, bytes);
                }
                catch (IOException)
                {
                }

                throw;
            }

            if (file.Size > CheckpointBytes) stream.Flush(true);
            return null;
        }
        catch (Exception error) when (Skippable(error))
        {
            for (var remaining = index; remaining < file.Parts.Length; remaining++)
                cache.Release(file.Parts[remaining].Guid);

            if (large) resume.ClearPartial();
            return error;
        }
    }

    private static async Task<FileStream> OpenTargetAsync(string target, string name, FileMode mode, CancellationToken cancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(target, mode, FileAccess.Write, FileShare.None,
                    1 << 16, FileOptions.SequentialScan);
            }
            catch (IOException error) when (FileLocks.IsLockViolation(error))
            {
                if (attempt >= 6) throw FileLocks.InUse(target, name, error);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellation).ConfigureAwait(false);
            }
        }
    }

    private bool Skippable(Exception error) => options.SkipMissing && error is ChunkMissingException;

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
        var networkEta = rate > 1024d && remaining > 0 ? remaining / rate : 0d;

        var progress = new DownloadProgress(
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
            TimeSpan.Zero,
            Volatile.Read(ref _currentFile),
            _throttle.Limit,
            _throttle.Reason);

        _workRate.Record(progress.WorkDone, now);
        var work = _workRate.PerSecond;
        var workEta = work > 1024d ? (progress.WorkTotal - progress.WorkDone) / work : 0d;

        return progress with { Eta = TimeSpan.FromSeconds(Math.Max(networkEta, workEta)) };
    }

    private void EnsureDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return;

        lock (_gate)
        {
            if (_createdDirectories.Contains(directory)) return;
            Directory.CreateDirectory(directory);
            _createdDirectories.Add(directory);
        }
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

internal readonly record struct FailedFile(string FileName, string Reason, bool NotHosted);

internal readonly record struct DownloadResult(
    int Files,
    int Chunks,
    long TransferredBytes,
    long WrittenBytes,
    TimeSpan Elapsed,
    IReadOnlyList<FailedFile> Failures);
