namespace PanelCut.Core.Models;

public sealed class Inventory
{
    public List<Panel> Panels { get; } = [];
    public List<Scrap> Scraps { get; } = [];

    public void Validate()
    {
        if (Panels.Any(panel => panel is null) || Scraps.Any(scrap => scrap is null))
            throw new ArgumentException("Inventory cannot contain null items.");
        Validation.UniqueIds(Panels.Select(panel => panel.Id).Concat(Scraps.Select(scrap => scrap.Id)));
    }
}