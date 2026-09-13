namespace Legendary_Sharp.Interface;

internal static class Prompt
{
    public static string? Text(string question, string? initial = null, Func<string, string?>? validate = null) =>
        TextInput.Ask(question, initial, validate);

    public static string? Path(string question, string? initial = null, bool mustExist = false)
    {
        var answer = TextInput.Ask(question, initial, value =>
        {
            var cleaned = Clean(value);
            if (cleaned.Length == 0) return "Enter a folder path.";
            if (mustExist && !Directory.Exists(cleaned)) return "That folder does not exist.";
            return null;
        }, completePaths: true);

        return answer is null ? null : Clean(answer);
    }

    public static int? Number(string question, int current, int minimum, int maximum)
    {
        var answer = TextInput.Ask(question, current.ToString(), value =>
            !int.TryParse(value.Trim(), out var parsed)
                ? "Enter a whole number."
                : parsed < minimum || parsed > maximum
                    ? $"Choose a value between {minimum} and {maximum}."
                    : null);

        return answer is null ? null : int.Parse(answer.Trim());
    }

    public static bool Confirm(string question, bool defaultValue = true) =>
        ConfirmPrompt.Ask(question, defaultValue) == true;

    public static bool? Ask(string question, bool defaultValue = true) =>
        ConfirmPrompt.Ask(question, defaultValue);

    public static string Clean(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"') trimmed = trimmed[1..^1];
        return trimmed.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }
}
