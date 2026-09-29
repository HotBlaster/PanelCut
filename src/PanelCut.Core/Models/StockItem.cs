namespace PanelCut.Core.Models;

public abstract class StockItem : IStockItem
{
    protected StockItem(double width, double height, Guid materialId, int quantity)
    {
        Width = width;
        Height = height;
        MaterialId = materialId;
        Quantity = quantity;
    }

    public Guid Id { get; set => field = Validation.Id(value); } = Guid.NewGuid();
    public double Width { get; set => field = Validation.Positive(value, nameof(Width)); }
    public double Height { get; set => field = Validation.Positive(value, nameof(Height)); }
    public Guid MaterialId { get; set => field = Validation.Id(value); }
    public string Label { get; set => field = value ?? throw new ArgumentNullException(nameof(Label)); } = string.Empty;
    public int Quantity { get; set => field = Validation.Quantity(value, 0); }
    public int Priority { get; set; }
    public decimal CostPerUnit
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(CostPerUnit));
    }
    public double EdgeTrim { get; set => field = Validation.NonNegative(value, nameof(EdgeTrim)); }
    public double UsableWidth => Width - EdgeTrim - EdgeTrim;
    public double UsableHeight => Height - EdgeTrim - EdgeTrim;
    public bool IsUsable => UsableWidth > 0 && UsableHeight > 0;
}