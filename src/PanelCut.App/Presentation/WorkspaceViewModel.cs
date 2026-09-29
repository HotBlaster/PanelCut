using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using PanelCut.Core.Models;
using PanelCut.Core.Persistence;

namespace PanelCut.App.Presentation;

public sealed class WorkspaceViewModel
{
    private readonly InventoryStore inventoryStore;
    private readonly ProjectStore projectStore = new();
    private readonly MaterialStore materialStore;
    public WorkspaceViewModel(string? inventoryPath = null, string? materialsPath = null)
    {
        inventoryStore = new InventoryStore(inventoryPath);
        materialStore = new MaterialStore(materialsPath ?? (inventoryPath is null ? null
            : Path.Combine(Path.GetDirectoryName(inventoryStore.FilePath)!, "materials.json")));
        if (string.Equals(InventoryPath, MaterialsPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Inventory and materials must use different files.");
    }
    public MaterialCatalogue Catalogue { get; private set; } = new();
    public ObservableCollection<MaterialRow> Materials { get; } = [];
    public ObservableCollection<MaterialOption> MaterialOptions { get; } = [];
    public ObservableCollection<string> MaterialTypes { get; } = [];
    public string MaterialsPath => materialStore.FilePath;
    public bool MaterialsReady { get; private set; }
    public Inventory Inventory { get; private set; } = new();
    public Project Project { get; private set; } = new();
    public ObservableCollection<StockRow> Panels { get; } = [];
    public ObservableCollection<StockRow> Scraps { get; } = [];
    public ObservableCollection<PartRow> Parts { get; } = [];
    public string? ProjectPath { get; private set; }
    public string InventoryPath => inventoryStore.FilePath;
    public bool InventoryReady { get; private set; }
    public bool IsDirty { get; private set; }

    public async Task LoadMaterialsAsync()
    {
        MaterialsReady = false;
        var loaded = await materialStore.LoadAsync();
        Catalogue = loaded;
        MaterialsReady = true;
        RestoreMaterialRows();
        RefreshMaterials();
    }

    public MaterialCatalogue MaterialCandidate()
    {
        var candidate = new MaterialCatalogue();
        candidate.Materials.AddRange(Materials.Select(row => row.ToModel()));
        candidate.Validate();
        return candidate;
    }

    public async Task CommitManualMaterialEditAsync(MaterialCatalogue candidate)
    {
        if (!MaterialsReady)
            throw new InvalidOperationException("Materials are unavailable. Reload the catalogue before editing.");
        if (Same(Catalogue, candidate))
            return;
        await materialStore.SaveAsync(candidate);
        Catalogue = candidate;
        RefreshMaterials();
    }

    public void RestoreMaterialRows()
    {
        Materials.Clear();
        foreach (var material in Catalogue.Materials)
            Materials.Add(new MaterialRow(material));
    }

    private void RefreshMaterials()
    {
        MaterialTypes.Clear();
        foreach (var type in Catalogue.Materials.Select(material => material.Type).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.CurrentCulture))
            MaterialTypes.Add(type);
        var options = Catalogue.Materials.Select(material => new MaterialOption(material.Id,
            $"{material.Name} / {material.Type} / {EditableRow.Format(material.Thickness)} mm")).ToArray();
        for (var index = 0; index < options.Length; index++)
        {
            if (index < MaterialOptions.Count)
                MaterialOptions[index] = options[index];
            else
                MaterialOptions.Add(options[index]);
        }
        while (MaterialOptions.Count > options.Length)
            MaterialOptions.RemoveAt(MaterialOptions.Count - 1);
        foreach (var row in Panels.Cast<MaterialBoundRow>().Concat(Scraps).Concat(Parts))
            row.Refresh();
    }

    public PartRow CreatePartRow(Part? part = null) => new(Project.Unit, part, () => Catalogue);
    public StockRow CreateStockRow(bool scrap, IStockItem? stock = null) => new(scrap, stock, () => Catalogue);

    public async Task LoadInventoryAsync()
    {
        InventoryReady = false;
        var loaded = await inventoryStore.LoadAsync();
        Inventory = loaded;
        InventoryReady = true;
        RestoreInventoryRows();
    }

    public Inventory InventoryCandidate()
    {
        var candidate = new Inventory();
        candidate.Panels.AddRange(Panels.Select(row => (Panel)row.ToModel()));
        candidate.Scraps.AddRange(Scraps.Select(row => (Scrap)row.ToModel()));
        candidate.Validate();
        return candidate;
    }

    public async Task<bool> CommitManualInventoryEditAsync(Inventory candidate)
    {
        if (!InventoryReady)
            throw new InvalidOperationException("Inventory is unavailable. Retry loading it before editing.");
        if (Same(Inventory, candidate))
            return false;
        await inventoryStore.SaveAsync(candidate);
        Inventory = candidate;
        return true;
    }

    public void RestoreInventoryRows()
    {
        Panels.Clear();
        Scraps.Clear();
        foreach (var panel in Inventory.Panels)
            Panels.Add(CreateStockRow(false, panel));
        foreach (var scrap in Inventory.Scraps)
            Scraps.Add(CreateStockRow(true, scrap));
    }

    public bool CommitProject(double kerfMillimetres, LengthUnit unit)
    {
        var candidate = new Project { KerfWidth = kerfMillimetres, Unit = unit };
        candidate.Parts.AddRange(Parts.Select(row => row.ToModel()));
        candidate.Validate();
        if (Same(Project, candidate))
            return false;
        Project = candidate;
        IsDirty = true;
        return true;
    }

    public void RefreshPartRows()
    {
        Parts.Clear();
        foreach (var part in Project.Parts)
            Parts.Add(CreatePartRow(part));
    }

    public async Task<CutListImport> ImportPartsAsync(string path)
    {
        var import = await CutListImporter.LoadAsync(path, Catalogue);
        foreach (var part in import.Parts)
            Parts.Add(CreatePartRow(part));
        return import;
    }

    public void NewProject() => AdoptProject(new Project(), null);
    public async Task OpenProjectAsync(string path) => AdoptProject(await projectStore.LoadAsync(path), path);
    public async Task SaveProjectAsync(string path)
    {
        if (string.Equals(Path.GetFullPath(path), InventoryPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(path), MaterialsPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A project cannot overwrite a repository file. Choose a different path.");
        await projectStore.SaveAsync(path, Project);
        ProjectPath = path;
        IsDirty = false;
    }

    public bool HasProjectDrafts(double kerf, LengthUnit unit)
    {
        try
        {
            var candidate = new Project { KerfWidth = kerf, Unit = unit };
            candidate.Parts.AddRange(Parts.Select(row => row.ToModel()));
            return !Same(Project, candidate);
        }
        catch (ArgumentException) { return true; }
    }

    private void AdoptProject(Project project, string? path)
    {
        Project = project;
        ProjectPath = path;
        IsDirty = false;
        RefreshPartRows();
    }

    private static bool Same<T>(T first, T second) => JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}