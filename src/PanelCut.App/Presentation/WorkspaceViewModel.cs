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
    private readonly BladeStore bladeStore;
    public WorkspaceViewModel(string? inventoryPath = null, string? materialsPath = null, string? bladesPath = null)
    {
        inventoryStore = new InventoryStore(inventoryPath);
        materialStore = new MaterialStore(materialsPath ?? (inventoryPath is null ? null
            : Path.Combine(Path.GetDirectoryName(inventoryStore.FilePath)!, "materials.json")));
        bladeStore = new BladeStore(bladesPath ?? (inventoryPath is null ? null
            : Path.Combine(Path.GetDirectoryName(inventoryStore.FilePath)!, "blades.json")));
        if (new[] { InventoryPath, MaterialsPath, BladesPath }.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 3)
            throw new ArgumentException("Inventory, materials and blades must use different files.");
    }
    public BladeCatalogue Blades { get; private set; } = new();
    public ObservableCollection<BladeRow> BladeRows { get; } = [];
    public ObservableCollection<BrandRow> BrandRows { get; } = [];
    public ObservableCollection<string> BrandNames { get; } = [];
    public string BladesPath => bladeStore.FilePath;
    public bool BladesReady { get; private set; }
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
        var options = Catalogue.Materials.Select(material => new MaterialOption(material.Id, material.Name)).ToArray();
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
    public BladeRow CreateBladeRow(Blade? blade = null) => new(() => Blades, blade);

    public async Task LoadBladesAsync()
    {
        BladesReady = false;
        Blades = await bladeStore.LoadAsync();
        BladesReady = true;
        RestoreBladeRows();
    }

    public BladeCatalogue BladeCandidate()
    {
        var candidate = new BladeCatalogue();
        candidate.Brands.AddRange(BrandRows.Select(row => row.ToModel()));
        foreach (var row in BladeRows)
        {
            var brandId = row.BrandId;
            if (row.NewBrand.Length > 0)
            {
                var brand = candidate.Brands.FirstOrDefault(existing =>
                    string.Equals(existing.Name, row.NewBrand, StringComparison.OrdinalIgnoreCase));
                if (brand is null)
                    candidate.Brands.Add(brand = new Brand(row.NewBrand));
                brandId = brand.Id;
            }
            else if (brandId is { } id && !candidate.Brands.Any(brand => brand.Id == id))
                throw new ArgumentException($"The brand of blade \"{row.Name}\" no longer exists. Choose another brand.");
            candidate.Blades.Add(row.ToModel(brandId));
        }
        candidate.Validate();
        return candidate;
    }

    public BladeCatalogue BrandDeletionCandidate(IReadOnlyCollection<Guid> brandIds)
    {
        var candidate = BladeCandidate();
        foreach (var brand in candidate.Brands.Where(brand => brandIds.Contains(brand.Id)))
        {
            var users = candidate.Blades.Count(blade => blade.BrandId == brand.Id);
            if (users > 0)
                throw new InvalidOperationException($"Brand \"{brand.Name}\" is used by {users} blade(s). Change those blades first.");
        }
        candidate.Brands.RemoveAll(brand => brandIds.Contains(brand.Id));
        return candidate;
    }

    public async Task CommitManualBladeEditAsync(BladeCatalogue candidate)
    {
        if (!BladesReady)
            throw new InvalidOperationException("Blades are unavailable. Reload blades before editing.");
        if (!Same(Blades, candidate))
            await bladeStore.SaveAsync(candidate);
        Blades = candidate;
        RestoreBladeRows();
    }

    public void RestoreBladeRows()
    {
        BrandRows.Clear();
        foreach (var brand in Blades.Brands)
            BrandRows.Add(new BrandRow(brand));
        BladeRows.Clear();
        foreach (var blade in Blades.Blades)
            BladeRows.Add(CreateBladeRow(blade));
        BrandNames.Clear();
        foreach (var name in Blades.Brands.Select(brand => brand.Name).Order(StringComparer.CurrentCultureIgnoreCase))
            BrandNames.Add(name);
    }

    public IReadOnlyList<BladeOption> ProjectBladeOptions()
    {
        var options = Blades.Blades.Select(blade => new BladeOption(blade.Id, BladeDisplay(blade)))
            .OrderBy(option => option.Display, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (Project.BladeId is { } id && options.All(option => option.Id != id))
            options.Insert(0, new BladeOption(id, "Missing blade - select another"));
        return options;
    }

    public Blade? ProjectBlade => Project.BladeId is { } id ? Blades.Blades.FirstOrDefault(blade => blade.Id == id) : null;

    private string BladeDisplay(Blade blade)
    {
        var brand = Blades.ResolveBrand(blade.BrandId)?.Name;
        return string.Join(" - ", new[] { blade.Name, brand, $"\u00D8{EditableRow.Format(blade.Diameter)} Z{blade.Teeth}",
            $"kerf {EditableRow.Format(blade.Kerf)} mm" }.Where(part => !string.IsNullOrEmpty(part)));
    }

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

    public bool CommitProject(Guid? bladeId, LengthUnit unit, CutPattern? pattern = null)
    {
        var candidate = new Project { BladeId = bladeId, Unit = unit, CutPattern = pattern ?? Project.CutPattern };
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
        if (new[] { InventoryPath, MaterialsPath, BladesPath }.Contains(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("A project cannot overwrite a repository file. Choose a different path.");
        await projectStore.SaveAsync(path, Project);
        ProjectPath = path;
        IsDirty = false;
    }

    public bool HasProjectDrafts(Guid? bladeId, LengthUnit unit)
    {
        try
        {
            var candidate = new Project { BladeId = bladeId, Unit = unit, CutPattern = Project.CutPattern };
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