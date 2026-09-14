using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal sealed class SettingsScreen(Session session)
{
    private enum Field
    {
        InstallRoot,
        LibraryFolders,
        Workers,
        Memory,
        Preset,
        CatalogAge,
        OfferUefn,
        Mouse,
        Reset,
        Back
    }

    public void Run()
    {
        while (true)
        {
            var settings = session.Settings;

            var choices = new List<Choice<Field>>
            {
                new(Field.InstallRoot, "Install root",
                    settings.EffectiveInstallRoot + (settings.UsesDefaultInstallRoot ? "   (default)" : string.Empty)),
                new(Field.LibraryFolders, "Library folders",
                    settings.LibraryRoots.Count > 0 ? string.Join(", ", settings.LibraryRoots) : "none"),
                new(Field.Workers, "Parallel workers", settings.Workers.ToString()),
                new(Field.Memory, "Memory budget", $"{settings.CacheBudgetMiB} MiB"),
                new(Field.Preset, "Default preset", Describe(settings.Preset)),
                new(Field.CatalogAge, "Release index max age", $"{settings.CatalogMaxAgeHours} hours"),
                new(Field.OfferUefn, "Offer UEFN after a download",
                    settings.OfferUefn ? "yes, when a matching release exists" : "no, never ask"),
                new(Field.Mouse, "Mouse and scrolling", DescribePointer(settings)),
                new(Field.Reset, "Reset to defaults", "restore every setting"),
                new(Field.Back, "Back", "return to the menu")
            };

            if (!SelectionList<Field>.TryPick("Settings", choices, out var field,
                    $"{Hints.Move}   {Hints.Edit}   esc back")) return;

            if (field == Field.Back) return;
            if (!Edit(field, settings)) continue;

            settings.Save(session.Paths);
        }
    }

    private bool Edit(Field field, AppSettings settings)
    {
        switch (field)
        {
            case Field.InstallRoot:
            {
                var value = Prompt.Path("Install root", settings.EffectiveInstallRoot);

                if (value is null) return false;
                settings.InstallRoot = value;
                return true;
            }

            case Field.LibraryFolders:
                return EditLibrary(settings);

            case Field.Workers:
            {
                var value = Prompt.Number("Parallel workers", settings.Workers, 1, 64);
                if (value is null) return false;
                settings.Workers = value.Value;
                return true;
            }

            case Field.Memory:
            {
                var value = Prompt.Number("Memory budget in MiB", settings.CacheBudgetMiB, 64, 16384);
                if (value is null) return false;
                settings.CacheBudgetMiB = value.Value;
                return true;
            }

            case Field.Preset:
            {
                var presets = TagPresets.All
                    .Select(preset => new Choice<string>(preset.Key, preset.Name, preset.Description))
                    .ToList();

                if (!SelectionList<string>.TryPick("Default install preset", presets, out var chosen)) return false;
                settings.Preset = chosen;
                return true;
            }

            case Field.CatalogAge:
            {
                var value = Prompt.Number("Refresh the release index after how many hours",
                    settings.CatalogMaxAgeHours, 1, 720);

                if (value is null) return false;
                settings.CatalogMaxAgeHours = value.Value;
                return true;
            }

            case Field.OfferUefn:
                settings.OfferUefn = !settings.OfferUefn;
                return true;

            case Field.Mouse:
            {
                var modes = new List<Choice<PointerMode>>
                {
                    new(PointerMode.Full, "Scroll and click",
                        "the wheel moves the highlight and a left click picks the row"),
                    new(PointerMode.Scroll, "Scroll only",
                        "the wheel moves the highlight, clicks are ignored"),
                    new(PointerMode.Off, "Arrow keys only",
                        "no mouse, and the console keeps its own text selection")
                };

                if (!SelectionList<PointerMode>.TryPick("Mouse and scrolling", modes, out var mode,
                        $"{Hints.Move}   {Hints.Choose}   esc back")) return false;

                settings.Mouse = AppSettings.NameOf(mode);
                ConsoleEx.ApplyPointer(mode);
                return true;
            }

            case Field.Reset:
            {
                if (Prompt.Ask("Reset every setting to its default?", false) != true) return false;

                settings.ResetToDefaults();
                settings.Save(session.Paths);
                ConsoleEx.ApplyPointer(settings.Pointer);

                Output.Blank();
                Output.Success("Settings reset.");
                ConsoleEx.PauseForKey("Press any key to continue.");
                return false;
            }

            default:
                return false;
        }
    }

    private static bool EditLibrary(AppSettings settings)
    {
        var actions = new List<Choice<int>> { new(0, "Add a folder", "scan it when listing installed builds") };

        actions.AddRange(settings.LibraryRoots
            .Select((root, index) => new Choice<int>(index + 1, "Remove", root)));

        if (!SelectionList<int>.TryPick("Library folders", actions, out var action)) return false;

        if (action == 0)
        {
            var added = Prompt.Path("Folder to scan", mustExist: true);
            if (added is null) return false;
            if (settings.LibraryRoots.Contains(added, StringComparer.OrdinalIgnoreCase)) return false;
            settings.LibraryRoots.Add(added);
            return true;
        }

        settings.LibraryRoots.RemoveAt(action - 1);
        return true;
    }

    private static string Describe(string key) => TagPresets.Find(key)?.Name ?? key;

    private static string DescribePointer(AppSettings settings) => settings.Pointer switch
    {
        PointerMode.Off => "arrow keys only",
        _ when !ConsoleInput.ScrollAvailable => "this console has no mouse support",
        PointerMode.Scroll => "scroll to move, arrows and enter to pick",
        _ => "scroll to move, click to pick"
    };

    public static string DefaultRoot() => AppSettings.DefaultInstallRoot();
}
