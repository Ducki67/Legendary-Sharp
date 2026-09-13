namespace Legendary_Sharp.Interface;

internal sealed record Choice<T>(T Value, string Label, string? Description = null, bool Enabled = true)
{
    public string SearchKey { get; } = (Label + " " + (Description ?? string.Empty)).ToLowerInvariant();
}
