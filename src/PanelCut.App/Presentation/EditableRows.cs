using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using PanelCut.Core.Models;

namespace PanelCut.App.Presentation;

public abstract class EditableRow : IEditableObject, INotifyPropertyChanged
{
    private Dictionary<PropertyInfo, object?>? backup;
    public Guid Id { get; init; } = Guid.NewGuid();
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsEditing => backup is not null;
    protected bool Restoring { get; private set; }
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public void BeginEdit()
    {
        backup ??= GetType().GetProperties().Where(property => property.CanWrite && property.Name != nameof(Id))
            .ToDictionary(property => property, property => property.GetValue(this));
    }

    public void CancelEdit()
    {
        if (backup is null)
            return;
        Restoring = true;
        try
        {
            foreach (var (property, value) in backup)
                property.SetValue(this, value);
            backup = null;
        }
        finally { Restoring = false; }
        Refresh();
    }

    public void EndEdit() => backup = null;

    public static string NormalizeDecimal(string text) =>
        text.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);

    public static double Number(string text, string name)
    {
        if (!double.TryParse(NormalizeDecimal(text), NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException($"{name}: enter a finite number.");
        return value;
    }

    public static int Integer(string text, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value))
            throw new ArgumentException($"{name}: enter a whole number.");
        return value;
    }

    public static string Format(double value) => value.ToString("G", CultureInfo.CurrentCulture);
}

public sealed class MaterialRow : EditableRow
{
    private bool loading;

    public MaterialRow(Material? material = null)
    {
        if (material is null)
            return;
        loading = true;
        Id = material.Id;
        Name = material.Name;
        Type = material.Type;
        Thickness = Format(material.Thickness);
        loading = false;
    }

    public string Name { get; set; } = "";
    public string Type
    {
        get;
        set
        {
            field = value;
            if (!loading && !Restoring && string.IsNullOrWhiteSpace(Name))
                Name = value;
            Refresh();
        }
    } = "";
    public string Thickness
    {
        get;
        set
        {
            field = value;
            if (!loading && !Restoring && !string.IsNullOrWhiteSpace(Type) && Name == Type
                && double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var thickness)
                && double.IsFinite(thickness) && thickness > 0)
                Name = $"{Type} {Format(thickness)} mm";
            Refresh();
        }
    } = "";
    public Material ToModel() => new(Name, Type, Number(Thickness, "Thickness")) { Id = Id };
}

public sealed record MaterialOption(Guid Id, string Display);
public sealed record BladeOption(Guid Id, string Display);

public sealed class BrandRow : EditableRow
{
    public BrandRow(Brand? brand = null)
    {
        if (brand is null)
            return;
        Id = brand.Id;
        Name = brand.Name;
    }

    public string Name { get; set; } = "";
    public Brand ToModel() => new(Name) { Id = Id };
}

public sealed class BladeRow : EditableRow
{
    private readonly Func<BladeCatalogue> catalogue;

    public BladeRow(Func<BladeCatalogue> catalogue, Blade? blade = null)
    {
        this.catalogue = catalogue;
        if (blade is null)
            return;
        Id = blade.Id;
        Name = blade.Name;
        Diameter = Format(blade.Diameter);
        Teeth = blade.Teeth.ToString(CultureInfo.CurrentCulture);
        Kerf = Format(blade.Kerf);
        BrandId = blade.BrandId;
        BrandCode = blade.BrandCode;
    }

    public string Name { get; set; } = "";
    public string Brand
    {
        get => NewBrand.Length > 0 ? NewBrand : catalogue().ResolveBrand(BrandId)?.Name ?? "";
        set
        {
            var name = (value ?? "").Trim();
            var existing = catalogue().Brands.FirstOrDefault(brand => string.Equals(brand.Name, name, StringComparison.OrdinalIgnoreCase));
            BrandId = existing?.Id;
            NewBrand = existing is null ? name : "";
            Refresh();
        }
    }
    public string BrandCode { get; set; } = "";
    public string Diameter { get; set; } = "";
    public string Teeth { get; set; } = "";
    public string Kerf { get; set; } = "";
    public Guid? BrandId { get; set; }
    // A typed brand name not yet in the catalogue; it is created when the blades are saved.
    public string NewBrand { get; set; } = "";

    public Blade ToModel(Guid? brandId) =>
        new(Name, Number(Diameter, "Diameter"), Integer(Teeth, "Teeth"), Number(Kerf, "Kerf"), brandId, BrandCode) { Id = Id };
}

public abstract class MaterialBoundRow(Func<MaterialCatalogue>? catalogue) : EditableRow
{
    protected Guid selectedMaterialId;
    protected Material? SelectedMaterial => catalogue?.Invoke().Materials.FirstOrDefault(material => material.Id == selectedMaterialId);
    public Guid MaterialId
    {
        get => selectedMaterialId;
        set
        {
            if (selectedMaterialId == value)
                return;
            selectedMaterialId = value;
            if (!Restoring)
                MaterialSelected();
            Refresh();
        }
    }
    public string MaterialType => SelectedMaterial?.Type ?? "";
    protected virtual void MaterialSelected() { }
    public void ValidateMaterial()
    {
        if (SelectedMaterial is null)
            throw new ArgumentException("Select a material from the catalogue.");
    }
}

public sealed class PartRow : MaterialBoundRow
{
    private readonly double? originalWidth;
    private readonly double? originalHeight;
    private readonly string? initialWidth;
    private readonly string? initialHeight;
    private readonly LengthUnit unit;

    public PartRow(LengthUnit unit, Part? part = null, Func<MaterialCatalogue>? catalogue = null) : base(catalogue)
    {
        this.unit = unit;
        if (part is null)
            return;
        Id = part.Id;
        originalWidth = part.Width;
        originalHeight = part.Height;
        Width = initialWidth = Format(UnitConversion.FromMillimetres(part.Width, unit));
        Height = initialHeight = Format(UnitConversion.FromMillimetres(part.Height, unit));
        Label = part.Label;
        selectedMaterialId = part.MaterialId;
        Quantity = part.Quantity.ToString(CultureInfo.CurrentCulture);
        Color = part.Color;
        IsEnabled = part.IsEnabled;
    }

    public string Label { get; set; } = "";
    public string Width { get; set; } = "";
    public string Height { get; set; } = "";
    public string Quantity { get; set; } = "1";
    public string Thickness => SelectedMaterial is { } material
        ? Format(UnitConversion.FromMillimetres(material.Thickness, unit)) : "";
    public string Color { get; set; } = Part.DefaultColor;
    public bool IsEnabled { get; set; } = true;

    public Part ToModel() => new(
        Width == initialWidth && originalWidth.HasValue ? originalWidth.Value : UnitConversion.ToMillimetres(Number(Width, "Width"), unit),
        Height == initialHeight && originalHeight.HasValue ? originalHeight.Value : UnitConversion.ToMillimetres(Number(Height, "Height"), unit),
        MaterialId, Integer(Quantity, "Quantity"))
    {
        Id = Id, Label = Label, Color = Color, IsEnabled = IsEnabled
    };
}

public sealed class StockRow : MaterialBoundRow
{
    public StockRow(bool isScrap, IStockItem? stock = null, Func<MaterialCatalogue>? catalogue = null) : base(catalogue)
    {
        IsScrap = isScrap;
        if (stock is null)
            return;
        Id = stock.Id;
        Width = Format(stock.Width);
        Height = Format(stock.Height);
        selectedMaterialId = stock.MaterialId;
        Label = stock.Label;
        Quantity = stock.Quantity.ToString(CultureInfo.CurrentCulture);
        Priority = stock.Priority.ToString(CultureInfo.CurrentCulture);
        CostPerUnit = stock.CostPerUnit.ToString(CultureInfo.CurrentCulture);
        IsEnabled = stock.IsEnabled;
        if (stock is Panel panel)
        {
            TrimTop = Format(panel.TrimTop);
            TrimBottom = Format(panel.TrimBottom);
            TrimLeft = Format(panel.TrimLeft);
            TrimRight = Format(panel.TrimRight);
        }
    }

    public bool IsScrap { get; }
    public string Width { get; set; } = "";
    public string Height { get; set; } = "";
    public string Thickness => SelectedMaterial is { } material ? Format(material.Thickness) : "";
    public string Label { get; set; } = "";
    protected override void MaterialSelected()
    {
        if (string.IsNullOrWhiteSpace(Label) && SelectedMaterial is { } material)
            Label = material.Name;
    }
    public string Quantity { get; set; } = "1";
    public string Priority { get; set; } = "0";
    public string CostPerUnit { get; set; } = "0";
    public string TrimTop { get; set; } = "0";
    public string TrimBottom { get; set; } = "0";
    public string TrimLeft { get; set; } = "0";
    public string TrimRight { get; set; } = "0";
    public bool IsEnabled { get; set; } = true;
    public string Usability
    {
        get
        {
            if (!IsEnabled)
                return "Disabled";
            try { return SelectedMaterial is null ? "Select material" : ToModel().IsUsable ? "Usable" : "Unusable trim"; }
            catch (ArgumentException) { return "Incomplete"; }
        }
    }

    public IStockItem ToModel()
    {
        var width = Number(Width, "Width");
        var height = Number(Height, "Height");
        var quantity = Integer(Quantity, "Quantity");
        IStockItem stock = IsScrap ? new Scrap(width, height, MaterialId, quantity) : new Panel(width, height, MaterialId, quantity);
        stock.Id = Id;
        stock.Label = Label;
        stock.Priority = Integer(Priority, "Priority");
        if (!decimal.TryParse(NormalizeDecimal(CostPerUnit), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.CurrentCulture, out var cost))
            throw new ArgumentException("Cost per unit: enter a decimal number.");
        stock.CostPerUnit = cost;
        stock.IsEnabled = IsEnabled;
        if (stock is Panel panel)
        {
            panel.TrimTop = Number(TrimTop, "Trim top");
            panel.TrimBottom = Number(TrimBottom, "Trim bottom");
            panel.TrimLeft = Number(TrimLeft, "Trim left");
            panel.TrimRight = Number(TrimRight, "Trim right");
        }
        return stock;
    }
}