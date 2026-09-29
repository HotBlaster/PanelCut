namespace PanelCut.Core.Models;

internal static class Validation
{
    internal static double Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "Must be finite and greater than zero.");
        return value;
    }

    internal static double NonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, "Must be finite and nonnegative.");
        return value;
    }

    internal static int Quantity(int value, int minimum)
    {
        if (value < minimum)
            throw new ArgumentOutOfRangeException(nameof(value), $"Quantity must be at least {minimum}.");
        return value;
    }

    internal static Guid Id(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An ID cannot be empty.", nameof(value));
        return value;
    }

    internal static string Material(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    internal static T Defined<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Unknown enum value.");
        return value;
    }

    internal static void UniqueIds(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (!seen.Add(Id(id)))
                throw new ArgumentException($"Duplicate ID: {id}.");
        }
    }
}