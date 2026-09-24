using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class ResumeLog : IDisposable
{
    private readonly string _path;
    private StreamWriter? _writer;

    private ResumeLog(string path) => _path = path;

    public static ResumeLog Open(string stateDirectory, string fileName = "completed.txt")
    {
        Directory.CreateDirectory(stateDirectory);
        return new ResumeLog(Path.Combine(stateDirectory, fileName));
    }

    public ResumeScan Scan(BuildManifest manifest, string installRoot)
    {
        if (!File.Exists(_path)) return new ResumeScan(new HashSet<string>(StringComparer.Ordinal), 0, 0);

        var byName = manifest.Files.ToDictionary(file => file.FileName, StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var missing = 0;
        var changed = 0;

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            var hash = line[..separator];
            var name = line[(separator + 1)..];
            if (!byName.TryGetValue(name, out var file)) continue;

            var target = Path.Combine(installRoot, file.ToNativePath());
            var info = new FileInfo(target);

            if (!info.Exists) missing++;
            else if (info.Length != file.Size || !hash.Equals(Convert.ToHexString(file.ShaHash), StringComparison.OrdinalIgnoreCase)) changed++;
            else completed.Add(name);
        }

        return new ResumeScan(completed, missing, changed);
    }

    public void MarkComplete(FileEntry file)
    {
        _writer ??= new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };

        _writer.WriteLine($"{Convert.ToHexString(file.ShaHash)}:{file.FileName}");
    }

    public void Clear()
    {
        Dispose();
        if (File.Exists(_path)) File.Delete(_path);
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }
}

internal readonly record struct ResumeScan(IReadOnlySet<string> Completed, int Missing, int Changed)
{
    public bool HasProgress => Completed.Count > 0;
}
