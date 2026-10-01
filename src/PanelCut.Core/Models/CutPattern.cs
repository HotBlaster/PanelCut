namespace PanelCut.Core.Models;

public enum CutPattern
{
    // Mixed cut directions chosen per placement.
    Optimal,
    // First cuts run parallel to the sheet's longer side, producing strips along its length.
    ByLength,
    // First cuts run parallel to the sheet's shorter side, producing strips across its width.
    ByWidth,
    // ByLength strips filled in order from the sheet edge, grouping parts of equal length.
    StripsByLength,
    // ByWidth strips filled in order from the sheet edge, grouping parts of equal length.
    StripsByWidth,
    // Like Optimal, but prefers fewer cuts over a larger remaining offcut.
    FewestCuts
}
