using System.Reflection;

namespace Legendary_Sharp.Application;

internal static class AppInfo
{
    public const string Name = "Legendary Sharp";

    public const string Command = "L#";

    public const string Author = "Ducki67";

    public const string Tagline =
        "Made by " + Author + "   ·   A C# rewrite of legendary, the Epic manifest installer";

    public const string Credit = "Manifest archives by polynite, VastBlast and Mast3rGamers";

    public const string Repository = "https://github.com/Ducki67/Legendary-Sharp";

    private static readonly System.Version? Assembly = typeof(AppInfo).Assembly.GetName().Version;

    public static readonly string Version = Assembly is null ? "1.0.0"
        : Assembly.Revision > 0 ? Assembly.ToString(4) : Assembly.ToString(3);

    public static readonly string Label = Assembly is null ? "1.0.0"
        : Assembly.Revision switch
        {
            <= 0 => Assembly.ToString(3),
            1 => $"{Assembly.ToString(3)} hotfix",
            var number => $"{Assembly.ToString(3)} hotfix {number}"
        };
}
