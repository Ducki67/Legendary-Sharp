using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

internal static class LaunchFolder
{
    private const string Marker = "builds";

    public static string? Detect()
    {
        string here;

        try
        {
            here = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (IsOrdinaryPlace(here) || IsInsideBuild(here)) return null;

        var segments = here.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment.Contains(Marker, StringComparison.OrdinalIgnoreCase)) ? here : null;
    }

    private static bool IsOrdinaryPlace(string here)
    {
        string[] places =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            AppContext.BaseDirectory
        ];

        return places
            .Where(place => place.Length > 0)
            .Any(place => string.Equals(Path.TrimEndingDirectorySeparator(place), here, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInsideBuild(string here)
    {
        for (var folder = new DirectoryInfo(here); folder is not null; folder = folder.Parent)
        {
            if (FortniteConstants.BuildContents.Any(name => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
                return true;

            try
            {
                if (FortniteConstants.BuildContents.All(name => Directory.Exists(Path.Combine(folder.FullName, name))))
                    return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }
}
