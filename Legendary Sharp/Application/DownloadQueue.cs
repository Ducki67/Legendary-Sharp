using System.Text.Json;
using System.Text.Json.Serialization;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Application;

internal sealed class QueueTicket
{
    [JsonPropertyName("build")] public string Build { get; set; } = string.Empty;

    [JsonPropertyName("folder")] public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;

    [JsonPropertyName("processId")] public int ProcessId { get; set; }

    [JsonPropertyName("enqueuedUtc")] public DateTime EnqueuedUtc { get; set; }

    [JsonPropertyName("startedUtc")] public DateTime? StartedUtc { get; set; }

    [JsonPropertyName("fraction")] public double Fraction { get; set; }

    [JsonPropertyName("etaSeconds")] public double EtaSeconds { get; set; }

    public bool IsFor(string folder) =>
        Folder.Length > 0 && string.Equals(Path.GetFullPath(Folder), Path.GetFullPath(folder),
            StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct QueueEntry(string Path, QueueTicket? Ticket, bool Mine);

internal readonly record struct QueueStatus(IReadOnlyList<QueueEntry> Entries, int Position)
{
    public bool IsMyTurn => Position == 0;

    public QueueTicket? Running => Entries.Count > 0 && !Entries[0].Mine ? Entries[0].Ticket : null;
}

internal static class DownloadQueue
{
    private const string Extension = ".ticket";

    public static QueueLease Join(AppPaths paths, QueueTicket ticket)
    {
        Directory.CreateDirectory(paths.Queue);

        ticket.ProcessId = Environment.ProcessId;
        ticket.EnqueuedUtc = DateTime.UtcNow;

        var name = $"{ticket.EnqueuedUtc.Ticks:D19}-{Environment.ProcessId}-{Guid.NewGuid():N}{Extension}";
        var path = Path.Combine(paths.Queue, name);

        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096,
            FileOptions.DeleteOnClose);

        var lease = new QueueLease(paths, path, stream, ticket);
        lease.Save();
        return lease;
    }

    public static IReadOnlyList<QueueEntry> Others(AppPaths paths) => Snapshot(paths, null, null);

    public static QueueTicket? Busy(AppPaths paths, string folder) =>
        Others(paths).Select(entry => entry.Ticket).FirstOrDefault(ticket => ticket?.IsFor(folder) == true);

    internal static List<QueueEntry> Snapshot(AppPaths paths, string? mine, QueueTicket? ticket)
    {
        var entries = new List<QueueEntry>();
        if (!Directory.Exists(paths.Queue)) return entries;

        IEnumerable<string> files;

        try
        {
            files = Directory.GetFiles(paths.Queue, "*" + Extension).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return entries;
        }

        foreach (var path in files)
        {
            if (string.Equals(path, mine, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new QueueEntry(path, ticket, true));
                continue;
            }

            if (!IsHeld(path)) continue;
            entries.Add(new QueueEntry(path, Read(path), false));
        }

        return entries;
    }

    private static bool IsHeld(string path)
    {
        try
        {
            File.Delete(path);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static QueueTicket? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return JsonSerializer.Deserialize(stream, AppJson.Default.QueueTicket);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

internal sealed class QueueLease : IDisposable
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(2);

    private readonly AppPaths _paths;
    private readonly string _path;
    private readonly FileStream _stream;
    private readonly QueueTicket _ticket;
    private readonly Lock _gate = new();

    private DateTime _lastReport = DateTime.MinValue;
    private bool _disposed;

    internal QueueLease(AppPaths paths, string path, FileStream stream, QueueTicket ticket)
    {
        _paths = paths;
        _path = path;
        _stream = stream;
        _ticket = ticket;
    }

    public bool Waited { get; private set; }

    public QueueStatus Check()
    {
        var entries = DownloadQueue.Snapshot(_paths, _path, _ticket);
        var position = entries.FindIndex(entry => entry.Mine);

        if (position < 0)
        {
            entries.Insert(0, new QueueEntry(_path, _ticket, true));
            position = 0;
        }

        if (position > 0) Waited = true;
        return new QueueStatus(entries, position);
    }

    public void Start()
    {
        lock (_gate)
        {
            _ticket.StartedUtc = DateTime.UtcNow;
            Save();
        }
    }

    public void Report(in DownloadProgress progress)
    {
        var now = DateTime.UtcNow;

        lock (_gate)
        {
            if (_disposed || now - _lastReport < ReportInterval) return;
            _lastReport = now;

            _ticket.Fraction = progress.TotalDownloadBytes > 0
                ? (double)progress.DownloadedBytes / progress.TotalDownloadBytes
                : 0d;
            _ticket.EtaSeconds = progress.Eta.TotalSeconds;
            Save();
        }
    }

    internal void Save()
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_ticket, AppJson.Default.QueueTicket);
            _stream.Position = 0;
            _stream.Write(bytes);
            _stream.SetLength(bytes.Length);
            _stream.Flush();
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
        }
    }
}
