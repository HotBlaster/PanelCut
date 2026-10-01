namespace PanelCut.Core.Models;

public interface IStockItem
{
    Guid Id { get; set; }
    double Width { get; set; }
    double Height { get; set; }
    Guid MaterialId { get; set; }
    string Label { get; set; }
    int Quantity { get; set; }
    int Priority { get; set; }
    decimal CostPerUnit { get; set; }
    double UsableWidth { get; }
    double UsableHeight { get; }
    bool IsUsable { get; }
}