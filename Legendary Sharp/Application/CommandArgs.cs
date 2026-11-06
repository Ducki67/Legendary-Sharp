namespace Legendary_Sharp.Application;

internal sealed class CommandArgs
{
    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "confirmed", "all", "yes", "skip-missing", "help", "version"
    };

    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "season", "preset", "tags", "folder"
    };

    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    private CommandArgs(string command, List<string> positionals)
    {
        Command = command;
        Positionals = positionals;
    }

    public string Command { get; }

    public IReadOnlyList<string> Positionals { get; }

    public bool Json => Has("json");

    public string? Argument => Positionals.Count > 0 ? string.Join(' ', Positionals) : null;

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public static CommandArgs Parse(IReadOnlyList<string> args)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];

            if (token is "-h" or "/?") token = "--help";
            if (token == "-v") token = "--version";

            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
            {
                positionals.Add(token);
                continue;
            }

            var name = token[2..];
            string? value = null;

            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }

            if (Switches.Contains(name))
            {
                if (value is not null) throw new CommandException($"--{name} does not take a value.");
                options[name] = string.Empty;
                continue;
            }

            if (!Values.Contains(name)) throw new CommandException($"Unknown option --{name}.");

            if (value is null)
            {
                if (index + 1 >= args.Count) throw new CommandException($"--{name} needs a value.");
                value = args[++index];
            }

            options[name] = value;
        }

        var command = options.ContainsKey("version") ? "version"
            : positionals.Count > 0 ? positionals[0].ToLowerInvariant()
            : "help";

        if (positionals.Count > 0 && !options.ContainsKey("version")) positionals.RemoveAt(0);

        var parsed = new CommandArgs(command, positionals);
        foreach (var (key, value) in options) parsed._options[key] = value;
        return parsed;
    }
}

internal sealed class CommandException(string message, int exitCode = CommandLine.UsageError) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
