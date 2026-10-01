using PanelCut.Core.Models;

namespace PanelCut.Core.Optimization;

public enum StockKind { Panel, Scrap }
public enum CutAxis { Horizontal, Vertical }

public sealed record LayoutRectangle(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;
}

public sealed record StockSnapshot(
    Guid Id, StockKind Kind, double Width, double Height, double Thickness,
    string Material, int Quantity, int Priority, decimal CostPerUnit,
    double TrimTop, double TrimBottom, double TrimLeft, double TrimRight,
    Guid MaterialId, string MaterialType, string Label)
{
    public double UsableWidth => Width - TrimLeft - TrimRight;
    public double UsableHeight => Height - TrimTop - TrimBottom;
}

public sealed record PartSnapshot(
    Guid Id, double Width, double Height, int Quantity, string Material,
    string Label, string Color,
    Guid MaterialId, string MaterialType, double Thickness);

public sealed record JobSettings(Guid BladeId, string BladeName, double KerfWidth, LengthUnit Unit);
public sealed record PlacedPart(PartSnapshot Part, int CopyIndex, LayoutRectangle Bounds, bool IsRotated);
public sealed record UnplacedPart(PartSnapshot Part, int Quantity);
public sealed record GuillotineCut(LayoutRectangle Region, CutAxis Axis, double Position, double KerfWidth);

public sealed class SheetLayout
{
    internal SheetLayout(StockSnapshot stock, int unitIndex, IEnumerable<PlacedPart> placements,
        IEnumerable<LayoutRectangle> remainingScraps, IEnumerable<GuillotineCut> cuts)
    {
        Stock = stock;
        UnitIndex = unitIndex;
        Placements = Array.AsReadOnly(placements.ToArray());
        RemainingScraps = Array.AsReadOnly(remainingScraps.ToArray());
        Cuts = Array.AsReadOnly(cuts.ToArray());
        StockArea = PackingNumbers.Area(stock.Width, stock.Height);
        PartArea = PackingNumbers.Sum(Placements.Select(placement => placement.Bounds.Area));
    }

    public StockSnapshot Stock { get; }
    public int UnitIndex { get; }
    public IReadOnlyList<PlacedPart> Placements { get; }
    public IReadOnlyList<LayoutRectangle> RemainingScraps { get; }
    public IReadOnlyList<GuillotineCut> Cuts { get; }
    public double StockArea { get; }
    public double PartArea { get; }
    public double WasteArea => Math.Max(0, StockArea - PartArea);
    public double WastePercentage => WasteArea / StockArea * 100;
}

public sealed class OptimizationResult
{
    internal OptimizationResult(JobSettings settings, IEnumerable<SheetLayout> sheets, IEnumerable<UnplacedPart> unplacedParts)
    {
        Settings = settings;
        Sheets = Array.AsReadOnly(sheets.ToArray());
        UnplacedParts = Array.AsReadOnly(unplacedParts.ToArray());
        TotalStockArea = PackingNumbers.Sum(Sheets.Select(sheet => sheet.StockArea));
        TotalPartArea = PackingNumbers.Sum(Sheets.Select(sheet => sheet.PartArea));
        foreach (var sheet in Sheets)
            TotalCost = checked(TotalCost + sheet.Stock.CostPerUnit);
    }

    public JobSettings Settings { get; }
    public IReadOnlyList<SheetLayout> Sheets { get; }
    public IReadOnlyList<UnplacedPart> UnplacedParts { get; }
    public bool IsComplete => UnplacedParts.Count == 0;
    public int StockItemsUsed => Sheets.Count;
    public decimal TotalCost { get; }
    public double TotalStockArea { get; }
    public double TotalPartArea { get; }
    public double WasteArea => Math.Max(0, TotalStockArea - TotalPartArea);
    public double WastePercentage => TotalStockArea == 0 ? 0 : WasteArea / TotalStockArea * 100;
}

internal static class PackingNumbers
{
    internal static double Area(double width, double height)
    {
        var area = width * height;
        if (!double.IsFinite(area) || area <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Area must be positive and representable as a finite double.");
        return area;
    }

    internal static double Sum(IEnumerable<double> values)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += value;
            if (!double.IsFinite(sum))
                throw new OverflowException("Total layout area exceeds the supported numeric range.");
        }
        return sum;
    }
}