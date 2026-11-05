using Legendary_Sharp.Downloader;
using Legendary_Sharp.Downloader.Manifest;
using Legendary_Sharp.Fortnite;
using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class TagPicker
{
    private static readonly TagPreset CustomTags = new("custom", "custom", "") { Includes = _ => false };

    private static readonly TagPreset FileTags = new("file", "file", "") { Includes = _ => false };

    private static List<string>? LoadTagFile(BuildManifest manifest)
    {
        Output.Blank();
        var path = Prompt.Path("Selective download file", mustExist: false);
        if (path is null) return null;

        try
        {
            var wanted = TagPresets.FromSelectiveDownloadFile(path);
            var usable = TagPresets.Intersect(wanted, manifest);

            Output.Blank();

            if (usable.Count == 0)
            {
                Output.Error("None of the tags in that file exist in this build.");
                Output.Hint("That file is probably for a different build era.");
                ConsoleEx.PauseForKey("Press any key to go back.");
                return null;
            }

            Output.Success($"{usable.Count} of {wanted.Count} tags from that file apply to this build.");
            Output.Pair("Size on disk", Format.Bytes(InstallTagCatalog.SizeOf(manifest, usable)));
            return usable;
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Output.Blank();
            Output.Error(error.Message);
            ConsoleEx.PauseForKey("Press any key to go back.");
            return null;
        }
    }

    public static List<string>? Pick(BuildManifest manifest, IReadOnlyList<string>? current = null)
    {
        var groups = InstallTagCatalog.Summarise(manifest);

        if (current is null && groups.Count <= 1)
        {
            Output.Blank();
            Output.Info($"This build has no install tags, the whole {Format.Bytes(manifest.InstallSize)} comes down.");
            return InstallTagCatalog.Flatten(groups);
        }

        if (current is null)
        {
            var choices = TagPresets.All
                .Select(preset => new Choice<TagPreset?>(
                    preset,
                    $"{preset.Name}  ({Format.Bytes(InstallTagCatalog.SizeOf(manifest, preset.Resolve(groups)))})",
                    preset.Description))
                .ToList();

            choices.Add(new Choice<TagPreset?>(CustomTags, "Pick tags myself", $"{groups.Count} groups available"));
            choices.Add(new Choice<TagPreset?>(FileTags, "Load from a file",
                "a selective download json, the kind shared for each build era"));

            if (!SelectionList<TagPreset?>.TryPick("How much of the build do you want?", choices, out var chosen))
                return null;

            if (ReferenceEquals(chosen, FileTags)) return LoadTagFile(manifest);
            if (!ReferenceEquals(chosen, CustomTags) && chosen is not null) return chosen.Resolve(groups);
        }

        var required = current is null
            ? groups
                .Select((group, index) => (group, index))
                .Where(item => item.group.Kind is TagKind.Untagged or TagKind.Core)
                .Select(item => item.index)
            : groups
                .Select((group, index) => (group, index))
                .Where(item => current.Count == 0 || item.group.Tags.Any(current.Contains))
                .Select(item => item.index);

        var tagChoices = groups
            .Select(group => new Choice<TagGroup>(
                group,
                group.Display,
                Format.Bytes(group.InstallSize).PadLeft(10) + "   " + group.Description))
            .ToList();

        return MultiSelectList<TagGroup>.TryPick("Select install tags", tagChoices, required, out var picked,
            selected => Format.Bytes(InstallTagCatalog.SizeOf(manifest, InstallTagCatalog.Flatten(selected))) +
                        " on disk")
            ? InstallTagCatalog.Flatten(picked)
            : null;
    }
}
