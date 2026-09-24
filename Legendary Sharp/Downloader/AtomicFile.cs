namespace Legendary_Sharp.Downloader;

internal static class AtomicFile
{
    private const int MoveAttempts = 5;

    public static void WriteAllText(string path, string contents) =>
        Write(path, temp => File.WriteAllText(temp, contents));

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, temp => File.WriteAllBytes(temp, contents));

    private static void Write(string path, Action<string> writer)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

        try
        {
            writer(temp);
            Replace(temp, path);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void Replace(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception error) when (attempt < MoveAttempts &&
                                          error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
