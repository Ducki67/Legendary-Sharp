using System.Collections.Concurrent;
using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class ChunkCache : IDisposable
{
    private readonly ConcurrentDictionary<ChunkGuid, Entry> _entries;
    private readonly SemaphoreSlim _slots;

    private int _resident;

    public ChunkCache(IReadOnlyDictionary<ChunkGuid, int> references, int slots)
    {
        _entries = new ConcurrentDictionary<ChunkGuid, Entry>(Environment.ProcessorCount, references.Count);
        foreach (var (guid, count) in references) _entries[guid] = new Entry(count);
        _slots = new SemaphoreSlim(slots, slots);
    }

    public int Resident => Math.Max(0, Volatile.Read(ref _resident));

    public async Task<bool> ReserveAsync(ChunkGuid guid, CancellationToken cancellation)
    {
        if (!_entries.TryGetValue(guid, out var entry)) return false;

        await _slots.WaitAsync(cancellation).ConfigureAwait(false);

        lock (entry)
        {
            if (entry.Remaining > 0)
            {
                entry.Reserved = true;
                return true;
            }
        }

        _slots.Release();
        return false;
    }

    public void Publish(ChunkGuid guid, byte[] data)
    {
        if (!_entries.TryGetValue(guid, out var entry)) return;

        lock (entry) entry.Published = true;
        if (entry.Source.TrySetResult(data)) Interlocked.Increment(ref _resident);
    }

    public void Abort(ChunkGuid guid, Exception error)
    {
        if (_entries.TryGetValue(guid, out var entry)) entry.Source.TrySetException(error);
    }

    public Task<byte[]> WaitAsync(ChunkGuid guid) =>
        _entries.TryGetValue(guid, out var entry)
            ? entry.Source.Task
            : Task.FromException<byte[]>(new InvalidOperationException($"Chunk {guid.ToHex()} was never scheduled."));

    public void Release(ChunkGuid guid)
    {
        if (!_entries.TryGetValue(guid, out var entry)) return;

        bool reserved;
        bool published;

        lock (entry)
        {
            if (entry.Remaining <= 0) return;
            if (--entry.Remaining > 0) return;
            reserved = entry.Reserved;
            published = entry.Published;
        }

        _entries.TryRemove(guid, out _);
        if (published) Interlocked.Decrement(ref _resident);
        if (reserved) _slots.Release();
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Entry(int remaining)
    {
        public readonly TaskCompletionSource<byte[]> Source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Remaining = remaining;

        public bool Reserved;

        public bool Published;
    }
}
