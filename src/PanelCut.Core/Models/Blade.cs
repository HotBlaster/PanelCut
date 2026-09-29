namespace PanelCut.Core.Models;

public sealed record Brand
{
    public Brand(string name) => Name = Validation.Material(name).Trim();

    public Guid Id { get; init => field = Validation.Id(value); } = Guid.NewGuid();
    public string Name { get; }
}

public sealed record Blade
{
    public Blade(string name, double diameter, int teeth, double kerf, Guid? brandId = null, string brandCode = "")
    {
        Name = Validation.Material(name);
        Diameter = Validation.Positive(diameter, nameof(diameter));
        Teeth = Validation.Quantity(teeth, 1);
        Kerf = Validation.NonNegative(kerf, nameof(kerf));
        BrandId = brandId is { } id ? Validation.Id(id) : null;
        ArgumentNullException.ThrowIfNull(brandCode);
        BrandCode = brandCode;
    }

    public Guid Id { get; init => field = Validation.Id(value); } = Guid.NewGuid();
    public string Name { get; }
    public double Diameter { get; }
    public int Teeth { get; }
    public double Kerf { get; }
    public Guid? BrandId { get; }
    public string BrandCode { get; }
}

public sealed class BladeCatalogue
{
    public List<Brand> Brands { get; } = [];
    public List<Blade> Blades { get; } = [];

    public void Validate()
    {
        if (Brands.Any(brand => brand is null))
            throw new ArgumentException("Brands cannot contain null entries.");
        if (Blades.Any(blade => blade is null))
            throw new ArgumentException("Blades cannot contain null entries.");
        Validation.UniqueIds(Brands.Select(brand => brand.Id));
        Validation.UniqueIds(Blades.Select(blade => blade.Id));
        var duplicate = Brands.GroupBy(brand => brand.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Brand \"{duplicate.Key}\" is defined more than once.");
        var brandIds = Brands.Select(brand => brand.Id).ToHashSet();
        var orphan = Blades.FirstOrDefault(blade => blade.BrandId is { } id && !brandIds.Contains(id));
        if (orphan is not null)
            throw new ArgumentException($"Blade \"{orphan.Name}\" references missing brand {orphan.BrandId}.");
    }

    public Blade Resolve(Guid id) => Blades.FirstOrDefault(blade => blade.Id == id)
        ?? throw new ArgumentException($"Blade {id} is missing from the blade list. Select an available blade.");

    public Brand? ResolveBrand(Guid? id) => id is null ? null : Brands.FirstOrDefault(brand => brand.Id == id);

    public bool IsBrandInUse(Guid brandId) => Blades.Any(blade => blade.BrandId == brandId);
}
