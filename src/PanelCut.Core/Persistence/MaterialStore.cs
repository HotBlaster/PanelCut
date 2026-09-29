using System.Text.Json.Serialization;
using PanelCut.Core.Models;

namespace PanelCut.Core.Persistence;

public sealed class MaterialStore
{
    private readonly JsonFileStore files = new();

    public MaterialStore(string? filePath = null) => FilePath = Path.GetFullPath(filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PanelCut", "materials.json"));

    public string FilePath { get; }

    public async Task<MaterialCatalogue> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await files.LoadAsync<MaterialDocument, MaterialCatalogue>(FilePath,
                document => document.ToModel(), cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    public Task SaveAsync(MaterialCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        catalogue.Validate();
        return files.SaveAsync(FilePath, new MaterialDocument
        {
            SchemaVersion = 1,
            Materials = catalogue.Materials.Select(material => new MaterialEntry
            {
                Id = material.Id, Name = material.Name, Type = material.Type, Thickness = material.Thickness
            }).ToList()
        }, cancellationToken);
    }
}

internal sealed class MaterialDocument
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public List<MaterialEntry> Materials { get; set; } = null!;

    internal MaterialCatalogue ToModel()
    {
        if (SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported materials schema version: {SchemaVersion}.");
        ArgumentNullException.ThrowIfNull(Materials);
        var catalogue = new MaterialCatalogue();
        foreach (var entry in Materials)
        {
            ArgumentNullException.ThrowIfNull(entry);
            catalogue.Materials.Add(new Material(entry.Name, entry.Type, entry.Thickness) { Id = entry.Id });
        }
        catalogue.Validate();
        return catalogue;
    }
}

internal sealed class MaterialEntry
{
    [JsonRequired] public Guid Id { get; set; }
    [JsonRequired] public string Name { get; set; } = null!;
    [JsonRequired] public string Type { get; set; } = null!;
    [JsonRequired] public double Thickness { get; set; }
}