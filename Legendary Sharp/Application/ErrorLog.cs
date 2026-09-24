using System.Globalization;

namespace Legendary_Sharp.Application;

internal static class ErrorLog
{
    private const long MaximumBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string? Write(Exception error, string context)
    {
        try
        {
            var path = Path.Combine(AppPaths.Create().Root, "log.txt");
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var entry = $"[{stamp}] {AppInfo.Name} {AppInfo.Version}, {context}{Environment.NewLine}" +
                        $"{error}{Environment.NewLine}{Environment.NewLine}";

            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaximumBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, entry);
            }

            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
