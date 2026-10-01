using System.Text.Json.Serialization;
using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

internal sealed class InventoryDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<PanelDocument> Panels { get; set; } = null!;
    [JsonRequired] public List<ScrapDocument> Scraps { get; set; } = null!;

    public InventoryDocument() { }

    internal InventoryDocument(Inventory inventory)
    {
        inventory.Validate();
        SchemaVersion = 3;
        Panels = inventory.Panels.Select(panel => new PanelDocument(panel)).ToList();
        Scraps = inventory.Scraps.Select(scrap => new ScrapDocument(scrap)).ToList();
    }

    internal Inventory ToModel()
    {
        if (SchemaVersion != 3)
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

internal abstract class StockDocument
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public double Width { get; set; }
    [JsonRequired] public double Height { get; set; }
    [JsonRequired] public Guid MaterialId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int Priority { get; set; }
    public decimal CostPerUnit { get; set; }
    public bool IsEnabled { get; set; } = true;

    protected StockDocument() { }

    protected StockDocument(IStockItem stock)
    {
        Id = stock.Id;
        Width = stock.Width;
        Height = stock.Height;
        MaterialId = stock.MaterialId;
        Label = stock.Label;
        Quantity = stock.Quantity;
        Priority = stock.Priority;
        CostPerUnit = stock.CostPerUnit;
        IsEnabled = stock.IsEnabled;
    }

    protected void ApplyTo(IStockItem stock)
    {
        stock.Id = Id;
        stock.Label = Label;
        stock.Priority = Priority;
        stock.CostPerUnit = CostPerUnit;
        stock.IsEnabled = IsEnabled;
    }
}

internal sealed class PanelDocument : StockDocument
{
    public double TrimTop { get; set; }
    public double TrimBottom { get; set; }
    public double TrimLeft { get; set; }
    public double TrimRight { get; set; }

    public PanelDocument() { }

    internal PanelDocument(Panel panel) : base(panel)
    {
        TrimTop = panel.TrimTop;
        TrimBottom = panel.TrimBottom;
        TrimLeft = panel.TrimLeft;
        TrimRight = panel.TrimRight;
    }

    internal Panel ToPanel()
    {
        var panel = new Panel(Width, Height, MaterialId, Quantity)
        {
            TrimTop = TrimTop, TrimBottom = TrimBottom, TrimLeft = TrimLeft, TrimRight = TrimRight
        };
        ApplyTo(panel);
        return panel;
    }
}

internal sealed class ScrapDocument : StockDocument
{
    public ScrapDocument() { }

    internal ScrapDocument(Scrap scrap) : base(scrap) { }

    internal Scrap ToScrap()
    {
        var scrap = new Scrap(Width, Height, MaterialId, Quantity);
        ApplyTo(scrap);
        return scrap;
    }
}

internal sealed class ProjectDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<PartDocument> Parts { get; set; } = null!;
    public Guid? BladeId { get; set; }
    public LengthUnit Unit { get; set; } = LengthUnit.Millimetres;
    public CutPattern CutPattern { get; set; } = CutPattern.Optimal;

    public ProjectDocument() { }

    internal ProjectDocument(Project project)
    {
        project.Validate();
        SchemaVersion = 3;
        Parts = project.Parts.Select(part => new PartDocument(part)).ToList();
        BladeId = project.BladeId;
        Unit = project.Unit;
        CutPattern = project.CutPattern;
    }

    internal Project ToModel()
    {
        if (SchemaVersion != 3)
            throw new InvalidDataException($"Unsupported project schema version: {SchemaVersion}.");
        ArgumentNullException.ThrowIfNull(Parts);
        var project = new Project { BladeId = BladeId, Unit = Unit, CutPattern = CutPattern };
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
    public string Color { get; set; } = Part.DefaultColor;
    public bool IsEnabled { get; set; } = true;

    // Legacy fields from older project files: accepted on load, never written.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool EdgeBandTop { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool EdgeBandBottom { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool EdgeBandLeft { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool EdgeBandRight { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GroupTag { get; set; }

    public PartDocument() { }

    internal PartDocument(Part part)
    {
        Id = part.Id;
        Width = part.Width;
        Height = part.Height;
        MaterialId = part.MaterialId;
        Quantity = part.Quantity;
        Label = part.Label;
        Color = part.Color;
        IsEnabled = part.IsEnabled;
    }

    internal Part ToModel() => new(Width, Height, MaterialId, Quantity)
    {
        Id = Id,
        Label = Label,
        Color = Color,
        IsEnabled = IsEnabled
    };
}