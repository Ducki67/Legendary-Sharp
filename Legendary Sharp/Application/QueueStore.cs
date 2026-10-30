using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legendary_Sharp.Downloader;

namespace Legendary_Sharp.Application;

internal sealed class SavedJob
{
    [JsonPropertyName("build")] public string Build { get; set; } = string.Empty;

    [JsonPropertyName("manifestId")] public string ManifestId { get; set; } = string.Empty;

    [JsonPropertyName("root")] public string Root { get; set; } = string.Empty;

    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }

    [JsonPropertyName("addon")] public bool IsAddon { get; set; }

    [JsonPropertyName("skipMissing")] public bool SkipMissing { get; set; }
}

internal sealed class SavedQueue
{
    [JsonPropertyName("processId")] public int ProcessId { get; set; }

    [JsonPropertyName("processStartedUtc")] public DateTime ProcessStartedUtc { get; set; }

    [JsonPropertyName("jobs")] public List<SavedJob> Jobs { get; set; } = [];
}

internal static class QueueStore
{
    private static readonly Lazy<DateTime> Started = new(() =>
    {
        using var self = Process.GetCurrentProcess();
        return self.StartTime.ToUniversalTime();
    });

    public static void Save(AppPaths paths, IEnumerable<DownloadJob> jobs)
    {
        var path = Path.Combine(paths.Pending, $"{Environment.ProcessId}.json");

        var saved = jobs
            .Where(job => job.State is not (JobState.Done or JobState.Failed or JobState.Cancelled))
            .Where(job => job.Request.RepairFiles is null)
            .Select(job => new SavedJob
            {
                Build = job.Title,
                ManifestId = job.Request.Source.ManifestId,
                Root = job.Root,
                Tags = job.Request.Tags is null ? null : [.. job.Request.Tags],
                IsAddon = job.Request.IsAddon,
                SkipMissing = job.Options.SkipMissing
            })
            .ToList();

        try
        {
            if (saved.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var queue = new SavedQueue
            {
                ProcessId = Environment.ProcessId,
                ProcessStartedUtc = Started.Value,
                Jobs = saved
            };

            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(queue, AppJson.Default.SavedQueue));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static List<SavedJob> TakeAbandoned(AppPaths paths)
    {
        var jobs = new List<SavedJob>();
        if (!Directory.Exists(paths.Pending)) return jobs;

        foreach (var path in Directory.GetFiles(paths.Pending, "*.json"))
        {
            var queue = Read(path);
            if (queue is not null && IsAlive(queue)) continue;

            var claimed = $"{path}.{Environment.ProcessId}.claimed";

            try
            {
                File.Move(path, claimed);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (Read(claimed) is { } owned) jobs.AddRange(owned.Jobs);

            try
            {
                File.Delete(claimed);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        return jobs;
    }

    private static SavedQueue? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppJson.Default.SavedQueue);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsAlive(SavedQueue queue)
    {
        if (queue.ProcessId == Environment.ProcessId)
            return Math.Abs((Started.Value - queue.ProcessStartedUtc).TotalSeconds) < 2;

        try
        {
            using var process = Process.GetProcessById(queue.ProcessId);
            return Math.Abs((process.StartTime.ToUniversalTime() - queue.ProcessStartedUtc).TotalSeconds) < 2;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}
