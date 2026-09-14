namespace Legendary_Sharp.Interface;

internal static class Hints
{
    public static string Move => ConsoleInput.ScrollAvailable ? "\u2191\u2193 or scroll" : "\u2191\u2193 move";

    public static string Jump => "tab ends";

    public static string Choose => ConsoleInput.ClickAvailable ? "\u23ce or click" : "\u23ce select";

    public static string Open => ConsoleInput.ClickAvailable ? "\u23ce or click to open" : "\u23ce open";

    public static string Edit => ConsoleInput.ClickAvailable ? "\u23ce or click to edit" : "\u23ce edit";

    public static string Toggle => ConsoleInput.ClickAvailable ? "space or click" : "space";
}
