namespace PanelCut.Core.Models;

public sealed class Scrap(double width, double height, Guid materialId, int quantity = 1)
    : StockItem(width, height, materialId, quantity)
{
    public Guid? OriginPanelId
    {
        get;
        set => field = value.HasValue ? Validation.Id(value.Value) : null;
    }
}