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
        var demand = project.Parts.Select(part =>
        {
            var material = Resolve(part.MaterialId);
            return new Demand(new PartSnapshot(part.Id, part.Width, part.Height, part.Quantity, material.Name,
                part.Label, part.EdgeBandTop, part.EdgeBandBottom, part.EdgeBandLeft,
                part.EdgeBandRight, part.GroupTag, material.Id, material.Type, material.Thickness));
        }).ToArray();
        var orderedDemand = demand.OrderByDescending(item => item.Area)
            .ThenByDescending(item => Math.Max(item.Part.Width, item.Part.Height)).ToArray();
        var stockItems = inventory.Panels.Cast<IStockItem>().Concat(inventory.Scraps)
            .Select(stock =>
            {
                var material = Resolve(stock.MaterialId);
                return new StockSnapshot(stock.Id, stock is Scrap ? StockKind.Scrap : StockKind.Panel,
                    stock.Width, stock.Height, material.Thickness, material.Name, stock.Quantity,
                    stock.Priority, stock.CostPerUnit, stock.EdgeTrim,
                    (stock as Scrap)?.OriginPanelId, material.Id, material.Type, stock.Label);
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
            for (var unit = 0; unit < stock.Quantity; unit++)
            {
                var packer = new GuillotinePacker(stock, settings.KerfWidth);
                var placements = new List<PlacedPart>();
                while (true)
                {
                    var placed = false;
                    foreach (var item in matching)
                    {
                        if (item.Remaining == 0)
                            continue;
                        if (!packer.TryPlace(item.Part, out var bounds, out var rotated))
                            continue;
                        placements.Add(new PlacedPart(item.Part, item.Part.Quantity - item.Remaining + 1, bounds!, rotated));
                        item.Remaining--;
                        placed = true;
                        break;
                    }
                    if (!placed)
                        break;
                }
                if (placements.Count == 0)
                    break;
                sheets.Add(new SheetLayout(stock, unit + 1, placements, packer.FreeRectangles, packer.Cuts));
                if (matching.All(item => item.Remaining == 0))
                    break;
            }
        }
        return new OptimizationResult(settings, sheets, demand.Where(item => item.Remaining > 0)
            .Select(item => new UnplacedPart(item.Part, item.Remaining)));
    }

    private sealed class Demand(PartSnapshot part)
    {
        internal PartSnapshot Part { get; } = part;
        internal int Remaining { get; set; } = part.Quantity;
        internal double Area { get; } = PackingNumbers.Area(part.Width, part.Height);
    }
}