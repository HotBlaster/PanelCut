using System.Text.Json.Serialization;
using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

public sealed class BladeStore
{
    private readonly JsonFileStore files = new();

    public BladeStore(string? filePath = null) => FilePath = Path.GetFullPath(filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PanelCut", "blades.json"));

    public string FilePath { get; }

    public async Task<BladeCatalogue> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await files.LoadAsync<BladeDocument, BladeCatalogue>(FilePath,
                document => document.ToModel(), cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    public Task SaveAsync(BladeCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        catalogue.Validate();
        return files.SaveAsync(FilePath, new BladeDocument
        {
            SchemaVersion = 1,
            Brands = catalogue.Brands.Select(brand => new BrandEntry { Id = brand.Id, Name = brand.Name }).ToList(),
            Blades = catalogue.Blades.Select(blade => new BladeEntry
            {
                Id = blade.Id, Name = blade.Name, Diameter = blade.Diameter, Teeth = blade.Teeth,
                Kerf = blade.Kerf, BrandId = blade.BrandId, BrandCode = blade.BrandCode
            }).ToList()
        }, cancellationToken);
    }
}

internal sealed class BladeDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<BrandEntry> Brands { get; set; } = null!;
    [JsonRequired] public List<BladeEntry> Blades { get; set; } = null!;

    internal BladeCatalogue ToModel()
    {
        if (SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported blades schema version: {SchemaVersion}.");
        ArgumentNullException.ThrowIfNull(Brands);
        ArgumentNullException.ThrowIfNull(Blades);
        var catalogue = new BladeCatalogue();
        foreach (var entry in Brands)
        {
            ArgumentNullException.ThrowIfNull(entry);
            catalogue.Brands.Add(new Brand(entry.Name) { Id = entry.Id });
        }
        foreach (var entry in Blades)
        {
            ArgumentNullException.ThrowIfNull(entry);
            catalogue.Blades.Add(new Blade(entry.Name, entry.Diameter, entry.Teeth, entry.Kerf,
                entry.BrandId, entry.BrandCode) { Id = entry.Id });
        }
        catalogue.Validate();
        return catalogue;
    }
}

internal sealed class BrandEntry
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public string Name { get; set; } = null!;
}

internal sealed class BladeEntry
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public string Name { get; set; } = null!;
    [JsonRequired] public double Diameter { get; set; }
    [JsonRequired] public int Teeth { get; set; }
    [JsonRequired] public double Kerf { get; set; }
    public Guid? BrandId { get; set; }
    public string BrandCode { get; set; } = string.Empty;
}
