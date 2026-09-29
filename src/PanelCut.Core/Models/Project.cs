namespace PanelCut.Core.Models;

public sealed class Project
{
    public List<Part> Parts { get; } = [];
    public double KerfWidth { get; set => field = Validation.NonNegative(value, nameof(KerfWidth)); }
    public LengthUnit Unit { get; set => field = Validation.Defined(value); } = LengthUnit.Millimetres;

    public void Validate()
    {
        if (Parts.Any(part => part is null))
            throw new ArgumentException("Project cannot contain null parts.");
        Validation.UniqueIds(Parts.Select(part => part.Id));
    }
}