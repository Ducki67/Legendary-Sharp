using Legendary_Sharp.Downloader.Manifest;

namespace Legendary_Sharp.Downloader;

internal sealed class ResumeLog : IDisposable
{
    public const string BaseLog = "completed.txt";
    public const string AddonLog = "uefn.txt";

    private readonly string _path;
    private StreamWriter? _writer;

    private ResumeLog(string path) => _path = path;

    public static ResumeLog Open(string stateDirectory, string fileName = BaseLog)
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

        foreach (var (hash, name) in Entries())
        {
            if (!byName.TryGetValue(name, out var file)) continue;

            var info = new FileInfo(Path.Combine(installRoot, file.ToNativePath()));

            if (!info.Exists) missing++;
            else if (info.Length != file.Size || !HashMatches(hash, file)) changed++;
            else completed.Add(name);
        }

        return new ResumeScan(completed, missing, changed);
    }

    public void MarkComplete(FileEntry file) => Writer().WriteLine($"{Convert.ToHexString(file.ShaHash)}:{file.FileName}");

    public void MarkComplete(IEnumerable<FileEntry> files)
    {
        var writer = Writer();
        writer.AutoFlush = false;

        try
        {
            foreach (var file in files) writer.WriteLine($"{Convert.ToHexString(file.ShaHash)}:{file.FileName}");
            writer.Flush();
        }
        finally
        {
            writer.AutoFlush = true;
        }
    }

    public void SavePartial(FileEntry file, int parts, long bytes) =>
        AtomicFile.WriteAllText(PartialPath, string.Join('\n',
            "v1", file.FileName, Convert.ToHexString(file.ShaHash), file.Size, parts, bytes));

    public PartialFile? LoadPartial(BuildManifest manifest, string installRoot)
    {
        var saved = ReadPartial();
        if (saved is null) return null;

        var file = manifest.Files.FirstOrDefault(entry => entry.FileName == saved.Value.Name);
        if (file is null || !HashMatches(saved.Value.Sha, file)) return null;
        if (saved.Value.Size != file.Size || saved.Value.Parts < 0 || saved.Value.Parts > file.Parts.Length) return null;

        long sum = 0;
        for (var index = 0; index < saved.Value.Parts; index++) sum += file.Parts[index].Size;
        if (sum != saved.Value.Bytes) return null;

        var info = new FileInfo(Path.Combine(installRoot, file.ToNativePath()));
        if (!info.Exists || info.Length != file.Size) return null;

        return new PartialFile(file.FileName, saved.Value.Parts, saved.Value.Bytes);
    }

    public void ClearPartial()
    {
        try
        {
            if (File.Exists(PartialPath)) File.Delete(PartialPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<FileEntry> TouchedFiles(BuildManifest manifest)
    {
        var byName = manifest.Files.ToDictionary(file => file.FileName, StringComparer.Ordinal);
        var touched = new Dictionary<string, FileEntry>(StringComparer.Ordinal);

        foreach (var (hash, name) in Entries())
            if (byName.TryGetValue(name, out var file) && HashMatches(hash, file))
                touched[name] = file;

        var saved = ReadPartial();
        if (saved is not null
            && byName.TryGetValue(saved.Value.Name, out var partial)
            && HashMatches(saved.Value.Sha, partial))
            touched[partial.FileName] = partial;

        return [.. touched.Values];
    }

    public void Forget(IReadOnlySet<string> fileNames)
    {
        Dispose();

        if (File.Exists(_path))
        {
            var kept = Entries()
                .Where(entry => !fileNames.Contains(entry.Name))
                .Select(entry => $"{entry.Hash}:{entry.Name}")
                .ToList();

            AtomicFile.WriteAllText(_path, kept.Count == 0 ? string.Empty : string.Join('\n', kept) + "\n");
        }

        var saved = ReadPartial();
        if (saved is not null && fileNames.Contains(saved.Value.Name)) ClearPartial();
    }

    public void Clear()
    {
        Dispose();
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(PartialPath)) File.Delete(PartialPath);
    }

    private StreamWriter Writer() => _writer ??= new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
    {
        AutoFlush = true
    };

    private IEnumerable<(string Hash, string Name)> Entries()
    {
        if (!File.Exists(_path)) yield break;

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            yield return (line[..separator], line[(separator + 1)..]);
        }
    }

    private static bool HashMatches(string hex, FileEntry file)
    {
        Span<char> expected = stackalloc char[file.ShaHash.Length * 2];
        return Convert.TryToHexString(file.ShaHash, expected, out _) && hex.AsSpan().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private string PartialPath => Path.ChangeExtension(_path, ".partial");

    private (string Name, string Sha, long Size, int Parts, long Bytes)? ReadPartial()
    {
        try
        {
            if (!File.Exists(PartialPath)) return null;

            var lines = File.ReadAllLines(PartialPath);
            if (lines.Length != 6 || lines[0] != "v1") return null;

            return (lines[1], lines[2], long.Parse(lines[3]), int.Parse(lines[4]), long.Parse(lines[5]));
        }
        catch (Exception error) when (error is IOException or FormatException or OverflowException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }
}

internal sealed record PartialFile(string FileName, int Parts, long Bytes);

internal readonly record struct ResumeScan(IReadOnlySet<string> Completed, int Missing, int Changed)
{
    public bool HasProgress => Completed.Count > 0;
}
