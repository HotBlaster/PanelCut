namespace PanelCut.Core.Optimization;

internal sealed class GuillotinePacker
{
    private readonly double kerf;
    private readonly List<LayoutRectangle> freeRectangles;
    private readonly List<GuillotineCut> cuts = [];

    internal GuillotinePacker(StockSnapshot stock, double kerf)
    {
        this.kerf = kerf;
        freeRectangles = [new(stock.EdgeTrim, stock.EdgeTrim, stock.UsableWidth, stock.UsableHeight)];
    }

    internal IReadOnlyList<LayoutRectangle> FreeRectangles => freeRectangles;
    internal IReadOnlyList<GuillotineCut> Cuts => cuts;

    internal bool TryPlace(PartSnapshot part, out LayoutRectangle? bounds, out bool rotated)
    {
        var bestIndex = -1;
        var bestArea = double.PositiveInfinity;
        var bestShortSide = double.PositiveInfinity;
        bounds = null;
        rotated = false;
        for (var index = 0; index < freeRectangles.Count; index++)
        {
            var region = freeRectangles[index];
            var orientationCount = part.Width != part.Height ? 2 : 1;
            for (var orientation = 0; orientation < orientationCount; orientation++)
            {
                var width = orientation == 0 ? part.Width : part.Height;
                var height = orientation == 0 ? part.Height : part.Width;
                if (width > region.Width || height > region.Height)
                    continue;
                var area = region.Area - width * height;
                var shortSide = Math.Min(region.Width - width, region.Height - height);
                if (area > bestArea || (area == bestArea && shortSide >= bestShortSide))
                    continue;
                bestIndex = index;
                bestArea = area;
                bestShortSide = shortSide;
                bounds = new LayoutRectangle(region.X, region.Y, width, height);
                rotated = orientation != 0;
            }
        }
        if (bestIndex < 0)
            return false;
        var selected = freeRectangles[bestIndex];
        freeRectangles.RemoveAt(bestIndex);
        if (selected.Height - bounds!.Height >= selected.Width - bounds.Width)
        {
            var strip = Split(selected, CutAxis.Horizontal, bounds.Height);
            Split(strip, CutAxis.Vertical, bounds.Width);
        }
        else
        {
            var strip = Split(selected, CutAxis.Vertical, bounds.Width);
            Split(strip, CutAxis.Horizontal, bounds.Height);
        }
        return true;
    }

    private LayoutRectangle Split(LayoutRectangle region, CutAxis axis, double length)
    {
        var horizontal = axis == CutAxis.Horizontal;
        var available = horizontal ? region.Height : region.Width;
        if (length == available)
            return region;
        var gap = Math.Min(kerf, available - length);
        var remainder = available - length - gap;
        var position = (horizontal ? region.Y : region.X) + length;
        cuts.Add(new GuillotineCut(region, axis, position, gap));
        if (remainder > 0)
        {
            var start = position + gap;
            if (kerf > 0 && start <= position)
                throw new ArgumentOutOfRangeException(nameof(kerf), "Kerf is too small to represent at this coordinate scale.");
            freeRectangles.Add(horizontal
                ? new LayoutRectangle(region.X, start, region.Width, remainder)
                : new LayoutRectangle(start, region.Y, remainder, region.Height));
        }
        return horizontal
            ? new LayoutRectangle(region.X, region.Y, region.Width, length)
            : new LayoutRectangle(region.X, region.Y, length, region.Height);
    }
}