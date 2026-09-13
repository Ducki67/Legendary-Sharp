using System.Reflection;

namespace Legendary_Sharp.Application;

internal static class AppInfo
{
    public const string Name = "Legendary Sharp";

    public const string Author = "Ducki67";

    public const string Tagline =
        "Made by " + Author + "   ·   A C# rewrite of legendary, the Epic manifest installer";

    public const string Credit = "Manifest archives by polynite, VastBlast and Mast3rGamers";

    public const string Repository = "";

    public static readonly string Version = Resolve();

    private static string Resolve()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
