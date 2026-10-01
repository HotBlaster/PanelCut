namespace PanelCut.Core.Models;

public sealed class Panel(double width, double height, Guid materialId, int quantity = 1)
    : StockItem(width, height, materialId, quantity)
{
    public double TrimTop { get; set => field = Validation.NonNegative(value, nameof(TrimTop)); }
    public double TrimBottom { get; set => field = Validation.NonNegative(value, nameof(TrimBottom)); }
    public double TrimLeft { get; set => field = Validation.NonNegative(value, nameof(TrimLeft)); }
    public double TrimRight { get; set => field = Validation.NonNegative(value, nameof(TrimRight)); }
    public override double UsableWidth => Width - TrimLeft - TrimRight;
    public override double UsableHeight => Height - TrimTop - TrimBottom;
}