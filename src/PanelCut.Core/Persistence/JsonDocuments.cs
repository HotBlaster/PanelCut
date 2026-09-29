using System.Text.Json.Serialization;
using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

internal sealed class InventoryDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<StockDocument> Panels { get; set; } = null!;
    [JsonRequired] public List<ScrapDocument> Scraps { get; set; } = null!;

    public InventoryDocument() { }

    internal InventoryDocument(Inventory inventory)
    {
        inventory.Validate();
        SchemaVersion = 2;
        Panels = inventory.Panels.Select(panel => new StockDocument(panel)).ToList();
        Scraps = inventory.Scraps.Select(scrap => new ScrapDocument(scrap)).ToList();
    }

    internal Inventory ToModel()
    {
        if (SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported inventory schema version: {SchemaVersion}.");
        ArgumentNullException.ThrowIfNull(Panels);
        ArgumentNullException.ThrowIfNull(Scraps);
        var inventory = new Inventory();
        foreach (var document in Panels)
        {
            ArgumentNullException.ThrowIfNull(document);
            inventory.Panels.Add(document.ToPanel());
        }
        foreach (var document in Scraps)
        {
            ArgumentNullException.ThrowIfNull(document);
            inventory.Scraps.Add(document.ToScrap());
        }
        inventory.Validate();
        return inventory;
    }
}

internal class StockDocument
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public double Width { get; set; }
    [JsonRequired] public double Height { get; set; }
    [JsonRequired] public Guid MaterialId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int Priority { get; set; }
    public decimal CostPerUnit { get; set; }
    public double EdgeTrim { get; set; }

    public StockDocument() { }

    internal StockDocument(IStockItem stock)
    {
        Id = stock.Id;
        Width = stock.Width;
        Height = stock.Height;
        MaterialId = stock.MaterialId;
        Label = stock.Label;
        Quantity = stock.Quantity;
        Priority = stock.Priority;
        CostPerUnit = stock.CostPerUnit;
        EdgeTrim = stock.EdgeTrim;
    }

    protected void ApplyTo(IStockItem stock)
    {
        stock.Id = Id;
        stock.Label = Label;
        stock.Priority = Priority;
        stock.CostPerUnit = CostPerUnit;
        stock.EdgeTrim = EdgeTrim;
    }

    internal Panel ToPanel()
    {
        var panel = new Panel(Width, Height, MaterialId, Quantity);
        ApplyTo(panel);
        return panel;
    }
}

internal sealed class ScrapDocument : StockDocument
{
    public Guid? OriginPanelId { get; set; }

    public ScrapDocument() { }

    internal ScrapDocument(Scrap scrap) : base(scrap)
    {
        OriginPanelId = scrap.OriginPanelId;
    }

    internal Scrap ToScrap()
    {
        var scrap = new Scrap(Width, Height, MaterialId, Quantity) { OriginPanelId = OriginPanelId };
        ApplyTo(scrap);
        return scrap;
    }
}

internal sealed class ProjectDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<PartDocument> Parts { get; set; } = null!;
    public double KerfWidth { get; set; }
    public LengthUnit Unit { get; set; } = LengthUnit.Millimetres;

    public ProjectDocument() { }

    internal ProjectDocument(Project project)
    {
        project.Validate();
        SchemaVersion = 2;
        Parts = project.Parts.Select(part => new PartDocument(part)).ToList();
        KerfWidth = project.KerfWidth;
        Unit = project.Unit;
    }

    internal Project ToModel()
    {
        if (SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported project schema version: {SchemaVersion}.");
        ArgumentNullException.ThrowIfNull(Parts);
        var project = new Project { KerfWidth = KerfWidth, Unit = Unit };
        foreach (var document in Parts)
        {
            ArgumentNullException.ThrowIfNull(document);
            project.Parts.Add(document.ToModel());
        }
        project.Validate();
        return project;
    }
}

internal sealed class PartDocument
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public double Width { get; set; }
    [JsonRequired] public double Height { get; set; }
    [JsonRequired] public Guid MaterialId { get; set; }
    public int Quantity { get; set; } = 1;
    public string Label { get; set; } = string.Empty;
    public bool EdgeBandTop { get; set; }
    public bool EdgeBandBottom { get; set; }
    public bool EdgeBandLeft { get; set; }
    public bool EdgeBandRight { get; set; }
    public string GroupTag { get; set; } = string.Empty;

    public PartDocument() { }

    internal PartDocument(Part part)
    {
        Id = part.Id;
        Width = part.Width;
        Height = part.Height;
        MaterialId = part.MaterialId;
        Quantity = part.Quantity;
        Label = part.Label;
        EdgeBandTop = part.EdgeBandTop;
        EdgeBandBottom = part.EdgeBandBottom;
        EdgeBandLeft = part.EdgeBandLeft;
        EdgeBandRight = part.EdgeBandRight;
        GroupTag = part.GroupTag;
    }

    internal Part ToModel() => new(Width, Height, MaterialId, Quantity)
    {
        Id = Id,
        Label = Label,
        EdgeBandTop = EdgeBandTop,
        EdgeBandBottom = EdgeBandBottom,
        EdgeBandLeft = EdgeBandLeft,
        EdgeBandRight = EdgeBandRight,
        GroupTag = GroupTag
    };
}