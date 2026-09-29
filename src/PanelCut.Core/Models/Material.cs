namespace PanelCut.Core.Models;

public sealed record Material
{
    public Material(string name, string type, double thickness)
    {
        Name = Validation.Material(name);
        Type = Validation.Material(type);
        Thickness = Validation.Positive(thickness, nameof(thickness));
    }

    public Guid Id { get; init => field = Validation.Id(value); } = Guid.NewGuid();
    public string Name { get; }
    public string Type { get; }
    public double Thickness { get; }
}

public sealed class MaterialCatalogue
{
    public List<Material> Materials { get; } = [];

    public void Validate()
    {
        if (Materials.Any(material => material is null))
            throw new ArgumentException("Materials cannot contain null entries.");
        Validation.UniqueIds(Materials.Select(material => material.Id));
        Validation.UniqueNames(Materials.Select(material => material.Name), "Material");
    }

    public Material Resolve(Guid id) => Materials.FirstOrDefault(material => material.Id == id)
        ?? throw new ArgumentException($"Material {id} is missing from the catalogue. Select an available material.");
}