namespace PanelCut.Core.Models;

/// <summary>High-visibility part colours with distinct hues, light enough for dark label text.</summary>
public static class PartPalette
{
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#FF6B6B", "#FFA94D", "#FFD43B", "#A9E34B", "#51CF66", "#20C997",
        "#3BC9DB", "#4DABF7", "#748FFC", "#B197FC", "#F783AC", "#D9A066",
    ];

    /// <summary>Picks a random palette colour, preferring the least used among <paramref name="used"/>.</summary>
    public static string Next(IEnumerable<string> used, Random random)
    {
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(random);
        var counts = used.GroupBy(color => color, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var usage = Colors.Select(color => (Color: color, Count: counts.GetValueOrDefault(color))).ToArray();
        var least = usage.Min(entry => entry.Count);
        var candidates = usage.Where(entry => entry.Count == least).ToArray();
        return candidates[random.Next(candidates.Length)].Color;
    }
}
