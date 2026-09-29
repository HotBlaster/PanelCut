namespace PanelCut.Core.Models;

public sealed class Panel(double width, double height, Guid materialId, int quantity = 1)
    : StockItem(width, height, materialId, quantity);