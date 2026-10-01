using PanelCut.Core.Models;

namespace PanelCut.Core.Optimization;

public sealed class PanelOptimizer
{
    public OptimizationResult OptimizePanels(Inventory inventory, Project project, MaterialCatalogue catalogue, BladeCatalogue blades)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(blades);
        catalogue.Validate();
        blades.Validate();
        inventory.Validate();
        project.Validate();

        var blade = blades.Resolve(project.BladeId
            ?? throw new ArgumentException("Select a blade for the project before optimizing."));
        var settings = new JobSettings(blade.Id, blade.Name, blade.Kerf, project.Unit);
        var materials = catalogue.Materials.ToDictionary(material => material.Id);
        Material Resolve(Guid id) => materials.TryGetValue(id, out var material) ? material
            : throw new ArgumentException($"Material {id} is missing from the catalogue. Select an available material.");
        var demand = project.Parts.Where(part => part.IsEnabled).Select(part =>
        {
            var material = Resolve(part.MaterialId);
            return new Demand(new PartSnapshot(part.Id, part.Width, part.Height, part.Quantity, material.Name,
                part.Label, part.Color, material.Id, material.Type, material.Thickness));
        }).ToArray();
        var orderedDemand = demand.OrderByDescending(item => item.Area)
            .ThenByDescending(item => Math.Max(item.Part.Width, item.Part.Height)).ToArray();
        var stockItems = inventory.Panels.Cast<IStockItem>().Concat(inventory.Scraps).Where(stock => stock.IsEnabled)
            .Select(stock =>
            {
                var material = Resolve(stock.MaterialId);
                var panel = stock as Panel;
                return new StockSnapshot(stock.Id, stock is Scrap ? StockKind.Scrap : StockKind.Panel,
                    stock.Width, stock.Height, material.Thickness, material.Name, stock.Quantity,
                    stock.Priority, stock.CostPerUnit,
                    panel?.TrimTop ?? 0, panel?.TrimBottom ?? 0, panel?.TrimLeft ?? 0, panel?.TrimRight ?? 0,
                    material.Id, material.Type, stock.Label);
            })
            .Where(stock => stock.Quantity > 0 && stock.UsableWidth > 0 && stock.UsableHeight > 0)
            .OrderBy(stock => stock.Priority).ToArray();
        var sheets = new List<SheetLayout>();
        foreach (var stock in stockItems)
        {
            if (demand.All(item => item.Remaining == 0))
                break;
            var matching = orderedDemand.Where(item =>
                string.Equals(item.Part.Material, stock.Material, StringComparison.Ordinal)
                && string.Equals(item.Part.MaterialType, stock.MaterialType, StringComparison.Ordinal)
                && item.Part.Thickness == stock.Thickness).ToArray();
            if (!matching.Any(item => item.Remaining > 0))
                continue;
            PackingNumbers.Area(stock.Width, stock.Height);
            var orders = Orders(matching);
            var heuristics = Heuristics(project.CutPattern, stock);
            var fewestCuts = project.CutPattern == CutPattern.FewestCuts;
            for (var unit = 0; unit < stock.Quantity; unit++)
            {
                SheetCandidate? best = null;
                foreach (var heuristic in heuristics)
                {
                    var candidate = Fill(stock, settings.KerfWidth, matching, orders[(int)heuristic.Order], heuristic);
                    if (best is null || candidate.IsBetterThan(best, fewestCuts))
                        best = candidate;
                }
                if (best!.Placements.Count == 0)
                    break;
                var placements = new List<PlacedPart>();
                foreach (var (index, bounds, rotated) in best.Placements)
                {
                    var item = matching[index];
                    placements.Add(new PlacedPart(item.Part, item.Part.Quantity - item.Remaining + 1, bounds, rotated));
                    item.Remaining--;
                }
                sheets.Add(new SheetLayout(stock, unit + 1, placements, best.Packer.FreeRectangles, best.Packer.Cuts));
                if (matching.All(item => item.Remaining == 0))
                    break;
            }
        }
        return new OptimizationResult(settings, sheets, demand.Where(item => item.Remaining > 0)
            .Select(item => new UnplacedPart(item.Part, item.Remaining)));
    }

    private static int[][] Orders(Demand[] matching)
    {
        var indexes = Enumerable.Range(0, matching.Length).ToArray();
        double Long(int index) => Math.Max(matching[index].Part.Width, matching[index].Part.Height);
        double Short(int index) => Math.Min(matching[index].Part.Width, matching[index].Part.Height);
        // Indexed by PartOrder; matching is already in area order.
        return
        [
            indexes,
            indexes.OrderByDescending(Long).ThenByDescending(Short).ToArray(),
            indexes.OrderByDescending(index => Long(index) + Short(index)).ThenByDescending(Long).ToArray(),
            indexes.OrderByDescending(Short).ThenByDescending(Long).ToArray()
        ];
    }

    // The first heuristic reproduces the pattern's classic layout and wins all ties.
    private static PackingHeuristic[] Heuristics(CutPattern pattern, StockSnapshot stock)
    {
        var lengthAxis = stock.UsableWidth >= stock.UsableHeight ? SplitRule.Horizontal : SplitRule.Vertical;
        var widthAxis = lengthAxis == SplitRule.Horizontal ? SplitRule.Vertical : SplitRule.Horizontal;
        var strips = pattern is CutPattern.StripsByLength or CutPattern.StripsByWidth;
        SplitRule[] splits = pattern switch
        {
            CutPattern.ByLength or CutPattern.StripsByLength => [lengthAxis],
            CutPattern.ByWidth or CutPattern.StripsByWidth => [widthAxis],
            _ => [SplitRule.Leftover, SplitRule.ReverseLeftover, SplitRule.MaxArea, SplitRule.MinArea,
                SplitRule.Horizontal, SplitRule.Vertical]
        };
        PartOrder[] orders = strips
            ? [PartOrder.LongSide, PartOrder.ShortSide, PartOrder.Area, PartOrder.Perimeter]
            : [PartOrder.Area, PartOrder.LongSide, PartOrder.Perimeter, PartOrder.ShortSide];
        return
        [
            .. from order in orders
            from split in splits
            from fit in strips
                ? [split == SplitRule.Horizontal ? FitRule.TopFirst : FitRule.LeftFirst]
                : new[] { FitRule.BestArea, FitRule.BestShortSide, FitRule.BestLongSide, FitRule.LeftFirst, FitRule.TopFirst }
            select new PackingHeuristic(order, fit, split)
        ];
    }

    private static SheetCandidate Fill(StockSnapshot stock, double kerf, Demand[] matching, int[] order, PackingHeuristic heuristic)
    {
        var packer = new GuillotinePacker(stock, kerf, heuristic.Fit, heuristic.Split);
        var used = new int[matching.Length];
        var placements = new List<(int Index, LayoutRectangle Bounds, bool Rotated)>();
        while (true)
        {
            var placed = false;
            foreach (var index in order)
            {
                if (matching[index].Remaining == used[index])
                    continue;
                if (!packer.TryPlace(matching[index].Part, out var bounds, out var rotated))
                    continue;
                placements.Add((index, bounds!, rotated));
                used[index]++;
                placed = true;
                break;
            }
            if (!placed)
                break;
        }
        // Summed per part in a fixed order so equal part sets compare exactly equal.
        var area = 0.0;
        for (var index = 0; index < matching.Length; index++)
            area += used[index] * matching[index].Area;
        return new SheetCandidate(packer, placements, area);
    }

    private sealed record SheetCandidate(GuillotinePacker Packer,
        List<(int Index, LayoutRectangle Bounds, bool Rotated)> Placements, double PartArea)
    {
        internal bool IsBetterThan(SheetCandidate other, bool fewestCuts)
        {
            if (PartArea != other.PartArea)
                return PartArea > other.PartArea;
            var offcut = Packer.LargestFreeArea.CompareTo(other.Packer.LargestFreeArea);
            var cuts = other.Packer.Cuts.Count.CompareTo(Packer.Cuts.Count);
            var (first, second) = fewestCuts ? (cuts, offcut) : (offcut, cuts);
            return first != 0 ? first > 0 : second > 0;
        }
    }

    private sealed class Demand(PartSnapshot part)
    {
        internal PartSnapshot Part { get; } = part;
        internal int Remaining { get; set; } = part.Quantity;
        internal double Area { get; } = PackingNumbers.Area(part.Width, part.Height);
    }
}