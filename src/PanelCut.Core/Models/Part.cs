namespace PanelCut.Core.Models;

public sealed class Part
{
    public Part(double width, double height, Guid materialId, int quantity = 1)
    {
        Width = width;
        Height = height;
        MaterialId = materialId;
        Quantity = quantity;
    }

    public Guid Id { get; set => field = Validation.Id(value); } = Guid.NewGuid();
    public double Width { get; set => field = Validation.Positive(value, nameof(Width)); }
    public double Height { get; set => field = Validation.Positive(value, nameof(Height)); }
    public int Quantity { get; set => field = Validation.Quantity(value, 1); }
    public Guid MaterialId { get; set => field = Validation.Id(value); }
    public string Label { get; set => field = value ?? throw new ArgumentNullException(nameof(Label)); } = string.Empty;
    public string Color { get; set => field = Validation.Color(value); } = DefaultColor;
    public bool IsEnabled { get; set; } = true;

    public const string DefaultColor = "#D5DDDB";
}