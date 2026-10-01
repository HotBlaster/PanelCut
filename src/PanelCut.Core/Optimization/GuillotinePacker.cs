using PanelCut.Core.Models;

namespace PanelCut.Core.Optimization;

internal enum PartOrder { Area, LongSide, Perimeter, ShortSide }

internal enum FitRule { BestArea, BestShortSide, BestLongSide, LeftFirst, TopFirst }

// Leftover: horizontal first when the vertical remainder is at least the horizontal one.
internal enum SplitRule { Leftover, ReverseLeftover, MaxArea, MinArea, Horizontal, Vertical }

internal readonly record struct PackingHeuristic(PartOrder Order, FitRule Fit, SplitRule Split);

internal sealed class GuillotinePacker
{
    private readonly double kerf;
    private readonly FitRule fit;
    private readonly SplitRule split;
    private readonly List<LayoutRectangle> freeRectangles;
    private readonly List<GuillotineCut> cuts = [];

    internal GuillotinePacker(StockSnapshot stock, double kerf, FitRule fit, SplitRule split)
    {
        this.kerf = kerf;
        this.fit = fit;
        this.split = split;
        freeRectangles = [new(stock.TrimLeft, stock.TrimTop, stock.UsableWidth, stock.UsableHeight)];
    }

    internal IReadOnlyList<LayoutRectangle> FreeRectangles => freeRectangles;
    internal IReadOnlyList<GuillotineCut> Cuts => cuts;
    internal double LargestFreeArea => freeRectangles.Count == 0 ? 0 : freeRectangles.Max(region => region.Area);

    internal bool TryPlace(PartSnapshot part, out LayoutRectangle? bounds, out bool rotated)
    {
        var bestIndex = -1;
        var best = (double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
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
                var key = Score(region, width, height);
                if (key.CompareTo(best) >= 0)
                    continue;
                bestIndex = index;
                best = key;
                bounds = new LayoutRectangle(region.X, region.Y, width, height);
                rotated = orientation != 0;
            }
        }
        if (bestIndex < 0)
            return false;
        var selected = freeRectangles[bestIndex];
        freeRectangles.RemoveAt(bestIndex);
        var placed = bounds!;
        if (HorizontalFirst(selected, placed))
        {
            var strip = Split(selected, CutAxis.Horizontal, placed.Height);
            Split(strip, CutAxis.Vertical, placed.Width);
        }
        else
        {
            var strip = Split(selected, CutAxis.Vertical, placed.Width);
            Split(strip, CutAxis.Horizontal, placed.Height);
        }
        return true;
    }

    private (double, double, double) Score(LayoutRectangle region, double width, double height)
    {
        var shortSide = Math.Min(region.Width - width, region.Height - height);
        var longSide = Math.Max(region.Width - width, region.Height - height);
        return fit switch
        {
            FitRule.BestShortSide => (shortSide, longSide, 0),
            FitRule.BestLongSide => (longSide, shortSide, 0),
            FitRule.LeftFirst => (region.X, region.Y, width),
            FitRule.TopFirst => (region.Y, region.X, height),
            _ => (region.Area - width * height, shortSide, 0)
        };
    }

    private bool HorizontalFirst(LayoutRectangle region, LayoutRectangle placed)
    {
        var right = region.Width - placed.Width;
        var below = region.Height - placed.Height;
        var horizontalLargest = Math.Max(region.Width * below, right * placed.Height);
        var verticalLargest = Math.Max(region.Height * right, placed.Width * below);
        return split switch
        {
            SplitRule.ReverseLeftover => below < right,
            SplitRule.MaxArea => horizontalLargest >= verticalLargest,
            SplitRule.MinArea => horizontalLargest < verticalLargest,
            SplitRule.Horizontal => true,
            SplitRule.Vertical => false,
            _ => below >= right
        };
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