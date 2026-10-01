using System.Text.Json;
using System.Text.Json.Nodes;
using PanelCut.Core.Models;
using PanelCut.Core.Persistence;

namespace PanelCut.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PanelCut.Tests", Guid.NewGuid().ToString("N"));
    private string InventoryPath => Path.Combine(directory, "stock", "inventory.json");
    private string ProjectPath => Path.Combine(directory, "jobs", "kitchen.panelcut.json");

    [Fact]
    public async Task MaterialsSaveIndependentlyAndRejectInvalidData()
    {
        var path = Path.Combine(directory, "materials.json");
        var store = new MaterialStore(path);
        Assert.Empty((await store.LoadAsync()).Materials);
        Assert.False(Directory.Exists(directory));
        var catalogue = new MaterialCatalogue();
        catalogue.Materials.Add(new Material("Oak", "Plywood", 18));
        await store.SaveAsync(catalogue);
        Assert.Equal(catalogue.Materials, (await store.LoadAsync()).Materials);
        var before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(catalogue, cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        catalogue.Materials.Add(catalogue.Materials[0]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(catalogue));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(InventoryPath));
        Assert.False(File.Exists(ProjectPath));
        await File.WriteAllTextAsync(path, "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        Assert.Equal("{}", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task BladesAndBrandsRoundTripAndRejectInvalidSaves()
    {
        var path = Path.Combine(directory, "blades.json");
        var store = new BladeStore(path);
        Assert.Empty((await store.LoadAsync()).Blades);
        Assert.False(Directory.Exists(directory));
        var catalogue = CreateBlades();
        await store.SaveAsync(catalogue);
        var loaded = await store.LoadAsync();
        Assert.Equal(catalogue.Brands, loaded.Brands);
        Assert.Equal(catalogue.Blades, loaded.Blades);
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            Assert.Equal(new[] { "blades", "brands", "schemaVersion" },
                json.RootElement.EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal(new[] { "brandCode", "brandId", "diameter", "id", "kerf", "name", "teeth" },
                json.RootElement.GetProperty("blades")[0].EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal(new[] { "id", "name" },
                json.RootElement.GetProperty("brands")[0].EnumerateObject().Select(property => property.Name).Order());
        }
        var before = await File.ReadAllBytesAsync(path);
        catalogue.Brands.Add(new Brand("FREUD"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(catalogue));
        catalogue.Brands.RemoveAt(catalogue.Brands.Count - 1);
        catalogue.Brands.RemoveAt(0);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(catalogue));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.Combine(directory, "materials.json")));
    }

    [Theory]
    [InlineData("blades", "id", null)]
    [InlineData("blades", "name", null)]
    [InlineData("blades", "diameter", null)]
    [InlineData("blades", "teeth", null)]
    [InlineData("blades", "kerf", null)]
    [InlineData("blades", "name", "\" \"")]
    [InlineData("blades", "diameter", "0")]
    [InlineData("blades", "teeth", "0")]
    [InlineData("blades", "teeth", "1.5")]
    [InlineData("blades", "kerf", "-1")]
    [InlineData("blades", "brandCode", "null")]
    [InlineData("blades", "brandId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("blades", "brandId", "\"6c1e3f39-3a6f-4d2e-9a61-2b1e8a0f5d11\"")]
    [InlineData("blades", "unknown", "true")]
    [InlineData("brands", "id", null)]
    [InlineData("brands", "name", null)]
    [InlineData("brands", "name", "\" \"")]
    [InlineData("brands", "name", "\"festool\"")]
    [InlineData("brands", "unknown", "true")]
    public async Task InvalidBladeEntriesAreRejectedWithoutOverwrite(string list, string field, string? value)
    {
        var path = Path.Combine(directory, "blades.json");
        var store = new BladeStore(path);
        await store.SaveAsync(CreateBlades());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var entry = document[list]![0]!.AsObject();
        if (value is null)
            entry.Remove(field);
        else
            entry[field] = JsonNode.Parse(value);
        var text = document.ToJsonString();
        await File.WriteAllTextAsync(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task OptionalBladeFieldsUseDefaults()
    {
        var path = Path.Combine(directory, "blades.json");
        await WriteJsonAsync(path, $$"""{"schemaVersion":1,"brands":[],"blades":[{"id":"{{Guid.NewGuid()}}","name":"Rip","diameter":300,"teeth":24,"kerf":0}]}""");
        var blade = Assert.Single((await new BladeStore(path).LoadAsync()).Blades);
        Assert.Null(blade.BrandId);
        Assert.Equal(string.Empty, blade.BrandCode);
        Assert.Equal(0, blade.Kerf);
    }

    [Fact]
    public void DefaultBladeLocationUsesApplicationData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PanelCut", "blades.json");
        Assert.Equal(Path.GetFullPath(expected), new BladeStore().FilePath);
    }

    [Theory]
    [InlineData("id", null)]
    [InlineData("name", null)]
    [InlineData("type", null)]
    [InlineData("thickness", null)]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("name", "\" \"")]
    [InlineData("type", "null")]
    [InlineData("thickness", "0")]
    [InlineData("thickness", "-18")]
    [InlineData("unknown", "true")]
    public async Task InvalidMaterialEntriesAreRejectedWithoutOverwrite(string field, string? value)
    {
        var path = Path.Combine(directory, "materials.json");
        var store = new MaterialStore(path);
        await store.SaveAsync(TestMaterials.Catalogue());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var entry = document["materials"]![0]!.AsObject();
        if (value is null)
            entry.Remove(field);
        else
            entry[field] = JsonNode.Parse(value);
        var text = document.ToJsonString();
        await File.WriteAllTextAsync(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void DefaultInventoryLocationUsesApplicationData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PanelCut", "inventory.json");
        Assert.Equal(Path.GetFullPath(expected), new InventoryStore().FilePath);
    }

    [Fact]
    public async Task MissingInventoryReturnsIndependentEmptyModelsWithoutWriting()
    {
        var store = new InventoryStore(InventoryPath);
        var first = await store.LoadAsync();
        var second = await store.LoadAsync();
        first.Panels.Add(new Panel(100, 80, TestMaterials.Id("Oak", 18)));
        Assert.Empty(second.Panels);
        Assert.Empty(second.Scraps);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task MissingProjectIsAnErrorWithoutWriting()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new ProjectStore().LoadAsync(ProjectPath));
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        await Assert.ThrowsAsync<FileNotFoundException>(() => new ProjectStore().LoadAsync(ProjectPath));
        Assert.False(File.Exists(ProjectPath));
    }

    [Fact]
    public async Task InventoryRoundTripsEveryFieldWithoutSerializingDerivedState()
    {
        var inventory = CreateInventory();
        var snapshot = JsonSerializer.Serialize(inventory);
        var store = new InventoryStore(InventoryPath);
        await store.SaveAsync(inventory);
        var bytes = await File.ReadAllBytesAsync(InventoryPath);
        var loaded = await store.LoadAsync();
        Assert.Equivalent(inventory, loaded, strict: true);
        Assert.NotSame(inventory.Panels[0], loaded.Panels[0]);
        Assert.Equal(snapshot, JsonSerializer.Serialize(inventory));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(InventoryPath));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "panels", "schemaVersion", "scraps" }, json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(3, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var panel = json.RootElement.GetProperty("panels")[0];
        Assert.False(panel.TryGetProperty("usableWidth", out _));
        Assert.False(panel.TryGetProperty("usableHeight", out _));
        Assert.False(panel.TryGetProperty("isUsable", out _));
        Assert.False(panel.TryGetProperty("originPanelId", out _));
        Assert.False(panel.TryGetProperty("thickness", out _));
        Assert.False(panel.TryGetProperty("material", out _));
        Assert.Equal(inventory.Panels[0].MaterialId, panel.GetProperty("materialId").GetGuid());
        Assert.Equal("Panel A", panel.GetProperty("label").GetString());
        Assert.Equal(new[] { "costPerUnit", "height", "id", "isEnabled", "label", "materialId", "priority", "quantity", "trimBottom", "trimLeft", "trimRight", "trimTop", "width" },
            panel.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal((5d, 10d, 15d, 20d), (panel.GetProperty("trimTop").GetDouble(), panel.GetProperty("trimBottom").GetDouble(),
            panel.GetProperty("trimLeft").GetDouble(), panel.GetProperty("trimRight").GetDouble()));
        Assert.Equal(new[] { "costPerUnit", "height", "id", "isEnabled", "label", "materialId", "priority", "quantity", "width" },
            json.RootElement.GetProperty("scraps")[0].EnumerateObject().Select(property => property.Name).Order());
        Assert.False(loaded.Scraps[0].IsEnabled);
        Assert.True(loaded.Panels[0].IsEnabled);
    }

    [Fact]
    public async Task ProjectRoundTripsEveryFieldWithoutTouchingInventory()
    {
        var inventory = CreateInventory();
        await new InventoryStore(InventoryPath).SaveAsync(inventory);
        var inventoryBytes = await File.ReadAllBytesAsync(InventoryPath);
        var snapshot = JsonSerializer.Serialize(inventory);
        var project = CreateProject();
        var store = new ProjectStore();
        await store.SaveAsync(ProjectPath, project);
        var loaded = await store.LoadAsync(ProjectPath);
        Assert.Equivalent(project, loaded, strict: true);
        Assert.NotSame(project.Parts[0], loaded.Parts[0]);
        Assert.Equal(inventoryBytes, await File.ReadAllBytesAsync(InventoryPath));
        Assert.Equal(snapshot, JsonSerializer.Serialize(inventory));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(ProjectPath));
        Assert.Equal(new[] { "bladeId", "cutPattern", "parts", "schemaVersion", "unit" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(new[] { "color", "height", "id", "isEnabled", "label", "materialId", "quantity", "width" },
            json.RootElement.GetProperty("parts")[0].EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("inches", json.RootElement.GetProperty("unit").GetString());
        Assert.True(loaded.Parts[0].IsEnabled);
        Assert.False(loaded.Parts[1].IsEnabled);
        Assert.Equal("byWidth", json.RootElement.GetProperty("cutPattern").GetString());
        Assert.Equal(254, json.RootElement.GetProperty("parts")[0].GetProperty("width").GetDouble());
        Assert.Equal(3, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(project.BladeId, loaded.BladeId);
    }

    [Fact]
    public async Task ExplicitSavesReplaceFilesButModelEditsDoNotAutosave()
    {
        var inventory = CreateInventory();
        var store = new InventoryStore(InventoryPath);
        await store.SaveAsync(inventory);
        var before = await File.ReadAllBytesAsync(InventoryPath);
        inventory.Panels[0].Quantity = 7;
        Assert.Equal(before, await File.ReadAllBytesAsync(InventoryPath));
        await store.SaveAsync(inventory);
        Assert.Equal(7, (await store.LoadAsync()).Panels[0].Quantity);
        var project = CreateProject();
        var projects = new ProjectStore();
        await projects.SaveAsync(ProjectPath, project);
        project.BladeId = null;
        await projects.SaveAsync(ProjectPath, project);
        Assert.Null((await projects.LoadAsync(ProjectPath)).BladeId);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OmittedOptionalFieldsUseDocumentedDefaults()
    {
        await WriteJsonAsync(ProjectPath, """{"schemaVersion":3,"parts":[]}""");
        var project = await new ProjectStore().LoadAsync(ProjectPath);
        Assert.Null(project.BladeId);
        Assert.Equal(LengthUnit.Millimetres, project.Unit);
        Assert.Equal(CutPattern.Optimal, project.CutPattern);
        var stock = new JsonObject
        {
            ["id"] = Guid.NewGuid(), ["width"] = 100, ["height"] = 80,
            ["materialId"] = TestMaterials.Id("Oak")
        };
        var document = new JsonObject
        {
            ["schemaVersion"] = 3, ["panels"] = new JsonArray(stock), ["scraps"] = new JsonArray()
        };
        await WriteJsonAsync(InventoryPath, document.ToJsonString());
        var panel = (await new InventoryStore(InventoryPath).LoadAsync()).Panels[0];
        Assert.Equal(1, panel.Quantity);
        Assert.Equal(0, panel.Priority);
        Assert.Equal((0d, 0d, 0d, 0d), (panel.TrimTop, panel.TrimBottom, panel.TrimLeft, panel.TrimRight));
        Assert.Equal(0m, panel.CostPerUnit);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"parts\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"parts\":[]}")]
    [InlineData("{\"schemaVersion\":4,\"parts\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":null}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[null]}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"kerfWidth\":2}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"bladeId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"bladeId\":\"blade\"}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"unit\":\"yards\"}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"unit\":1}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"cutPattern\":\"diagonal\"}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"cutPattern\":1}")]
    [InlineData("{\"schemaVersion\":3,\"parts\":[],\"inventory\":{}}")]
    public async Task InvalidProjectFilesAreRejectedWithoutOverwrite(string text)
    {
        await WriteJsonAsync(ProjectPath, text);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(ProjectPath));
        Assert.Contains(ProjectPath, error.Message);
        Assert.Equal(text, await File.ReadAllTextAsync(ProjectPath));
        Assert.False(File.Exists(InventoryPath));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"panels\":[],\"scraps\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"panels\":[],\"scraps\":[]}")]
    [InlineData("{\"schemaVersion\":4,\"panels\":[],\"scraps\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"panels\":null,\"scraps\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"panels\":[null],\"scraps\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"panels\":[],\"scraps\":[null]}")]
    public async Task InvalidInventoryFilesAreRejectedWithoutOverwrite(string text)
    {
        await WriteJsonAsync(InventoryPath, text);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(InventoryPath).LoadAsync());
        Assert.Contains(InventoryPath, error.Message);
        Assert.Equal(text, await File.ReadAllTextAsync(InventoryPath));
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("materialId")]
    [InlineData("id")]
    public async Task RequiredStockFieldsCannotBeOmitted(string fieldName)
    {
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(InventoryPath))!;
        document["panels"]![0]!.AsObject().Remove(fieldName);
        await File.WriteAllTextAsync(InventoryPath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(InventoryPath).LoadAsync());
    }

    [Theory]
    [InlineData("width", "0")]
    [InlineData("height", "-1")]
    [InlineData("materialId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("materialId", "null")]
    [InlineData("materialId", "\" \"")]
    [InlineData("label", "null")]
    [InlineData("quantity", "-1")]
    [InlineData("costPerUnit", "-0.01")]
    [InlineData("trimTop", "-1")]
    [InlineData("trimBottom", "-1")]
    [InlineData("trimLeft", "-0.5")]
    [InlineData("trimRight", "-1")]
    [InlineData("edgeTrim", "0")]
    [InlineData("unknownField", "true")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000000\"")]
    public async Task InvalidStockValuesCannotBypassModelValidation(string fieldName, string value)
    {
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(InventoryPath))!;
        document["panels"]![0]![fieldName] = JsonNode.Parse(value);
        var text = document.ToJsonString();
        await File.WriteAllTextAsync(InventoryPath, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(InventoryPath).LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(InventoryPath));
    }

    [Theory]
    [InlineData("trimTop")]
    [InlineData("edgeTrim")]
    [InlineData("originPanelId")]
    public async Task ScrapsRejectTrimAndOriginFields(string fieldName)
    {
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(InventoryPath))!;
        document["scraps"]![0]![fieldName] = fieldName == "originPanelId" ? JsonValue.Create(Guid.NewGuid()) : JsonValue.Create(0);
        await File.WriteAllTextAsync(InventoryPath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(InventoryPath).LoadAsync());
    }

    [Fact]
    public async Task DocumentTypesCannotBeConfused()
    {
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        await new ProjectStore().SaveAsync(ProjectPath, CreateProject());
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(InventoryPath));
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(ProjectPath).LoadAsync());
    }

    [Fact]
    public async Task InvalidSavesPreserveExistingFilesAndDoNotCreateDirectories()
    {
        var inventory = CreateInventory();
        var store = new InventoryStore(InventoryPath);
        await store.SaveAsync(inventory);
        var before = await File.ReadAllBytesAsync(InventoryPath);
        inventory.Scraps[0].Id = inventory.Panels[0].Id;
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(inventory));
        Assert.Equal(before, await File.ReadAllBytesAsync(InventoryPath));
        var project = CreateProject();
        var projects = new ProjectStore();
        await projects.SaveAsync(ProjectPath, project);
        var projectBytes = await File.ReadAllBytesAsync(ProjectPath);
        project.Parts.Add(project.Parts[0]);
        await Assert.ThrowsAsync<ArgumentException>(() => projects.SaveAsync(ProjectPath, project));
        Assert.Equal(projectBytes, await File.ReadAllBytesAsync(ProjectPath));
        var newPath = Path.Combine(directory, "invalid", "inventory.json");
        await Assert.ThrowsAsync<ArgumentException>(() => new InventoryStore(newPath).SaveAsync(inventory));
        Assert.False(Directory.Exists(Path.GetDirectoryName(newPath)));
    }

    [Fact]
    public async Task CancelledSavesLeaveExistingDataIntact()
    {
        var store = new InventoryStore(InventoryPath);
        var inventory = CreateInventory();
        await store.SaveAsync(inventory);
        var before = await File.ReadAllBytesAsync(InventoryPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        inventory.Panels.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(inventory, cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(InventoryPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task LockedDestinationPreservesOldFileAndCleansTemporaryFileOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new InventoryStore(InventoryPath);
        var inventory = CreateInventory();
        await store.SaveAsync(inventory);
        var before = await File.ReadAllBytesAsync(InventoryPath);
        await using (var locked = new FileStream(InventoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            inventory.Panels.Clear();
            await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(inventory));
            await Assert.ThrowsAnyAsync<IOException>(() => store.LoadAsync());
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(InventoryPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DuplicateIdsAreRejectedWhenLoadingEitherDocument()
    {
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        var inventory = JsonNode.Parse(await File.ReadAllTextAsync(InventoryPath))!;
        inventory["scraps"]![0]!["id"] = inventory["panels"]![0]!["id"]!.DeepClone();
        await File.WriteAllTextAsync(InventoryPath, inventory.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventoryStore(InventoryPath).LoadAsync());
        await new ProjectStore().SaveAsync(ProjectPath, CreateProject());
        var project = JsonNode.Parse(await File.ReadAllTextAsync(ProjectPath))!;
        project["parts"]![1]!["id"] = project["parts"]![0]!["id"]!.DeepClone();
        await File.WriteAllTextAsync(ProjectPath, project.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(ProjectPath));
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("materialId")]
    [InlineData("id")]
    public async Task RequiredPartFieldsCannotBeOmitted(string fieldName)
    {
        await new ProjectStore().SaveAsync(ProjectPath, CreateProject());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(ProjectPath))!;
        document["parts"]![0]!.AsObject().Remove(fieldName);
        await File.WriteAllTextAsync(ProjectPath, document.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(ProjectPath));
    }

    [Theory]
    [InlineData("width", "0")]
    [InlineData("height", "-1")]
    [InlineData("quantity", "0")]
    [InlineData("quantity", "1.5")]
    [InlineData("materialId", "null")]
    [InlineData("label", "null")]
    [InlineData("color", "null")]
    [InlineData("color", "\"red\"")]
    [InlineData("unknownField", "true")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000000\"")]
    public async Task InvalidPartFieldsCannotBypassModelValidation(string fieldName, string value)
    {
        await new ProjectStore().SaveAsync(ProjectPath, CreateProject());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(ProjectPath))!;
        document["parts"]![0]![fieldName] = JsonNode.Parse(value);
        var text = document.ToJsonString();
        await File.WriteAllTextAsync(ProjectPath, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(ProjectPath));
        Assert.Equal(text, await File.ReadAllTextAsync(ProjectPath));
    }

    [Fact]
    public async Task MinimalPartAndScrapDocumentsUseDefaults()
    {
        await new ProjectStore().SaveAsync(ProjectPath, CreateProject());
        var project = JsonNode.Parse(await File.ReadAllTextAsync(ProjectPath))!;
        var part = project["parts"]![0]!.AsObject();
        foreach (var name in new[] { "quantity", "label", "color", "isEnabled" })
            part.Remove(name);
        await File.WriteAllTextAsync(ProjectPath, project.ToJsonString());
        var loadedPart = (await new ProjectStore().LoadAsync(ProjectPath)).Parts[0];
        Assert.Equal(1, loadedPart.Quantity);
        Assert.Equal(string.Empty, loadedPart.Label);
        Assert.Equal(Part.DefaultColor, loadedPart.Color);
        Assert.True(loadedPart.IsEnabled);
        await new InventoryStore(InventoryPath).SaveAsync(CreateInventory());
        var inventory = JsonNode.Parse(await File.ReadAllTextAsync(InventoryPath))!;
        var scrap = inventory["scraps"]![0]!.AsObject();
        foreach (var name in new[] { "quantity", "label", "priority", "costPerUnit", "isEnabled" })
            scrap.Remove(name);
        await File.WriteAllTextAsync(InventoryPath, inventory.ToJsonString());
        var loadedScrap = (await new InventoryStore(InventoryPath).LoadAsync()).Scraps[0];
        Assert.Equal(1, loadedScrap.Quantity);
        Assert.Equal(string.Empty, loadedScrap.Label);
        Assert.Equal(0, loadedScrap.Priority);
        Assert.Equal(0m, loadedScrap.CostPerUnit);
        Assert.True(loadedScrap.IsEnabled);
    }

    [Fact]
    public async Task LegacyEdgeBandAndGroupFieldsAreAcceptedAndDroppedOnSave()
    {
        var store = new ProjectStore();
        await store.SaveAsync(ProjectPath, CreateProject());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(ProjectPath))!;
        var part = document["parts"]![0]!.AsObject();
        part.Remove("color");
        part["edgeBandTop"] = true;
        part["edgeBandBottom"] = false;
        part["edgeBandLeft"] = true;
        part["edgeBandRight"] = false;
        part["groupTag"] = "Kitchen";
        await File.WriteAllTextAsync(ProjectPath, document.ToJsonString());
        var loaded = await store.LoadAsync(ProjectPath);
        Assert.Equal(Part.DefaultColor, loaded.Parts[0].Color);
        await store.SaveAsync(ProjectPath, loaded);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(ProjectPath));
        Assert.Equal(new[] { "color", "height", "id", "isEnabled", "label", "materialId", "quantity", "width" },
            json.RootElement.GetProperty("parts")[0].EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task DuplicateMaterialAndBladeNamesAreRejectedOnLoad()
    {
        var materialsPath = Path.Combine(directory, "materials.json");
        await new MaterialStore(materialsPath).SaveAsync(TestMaterials.Catalogue());
        var materials = JsonNode.Parse(await File.ReadAllTextAsync(materialsPath))!;
        materials["materials"]![1]!["name"] = " OAK ";
        await File.WriteAllTextAsync(materialsPath, materials.ToJsonString());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new MaterialStore(materialsPath).LoadAsync());
        Assert.Contains("more than once", error.Message);
        var bladesPath = Path.Combine(directory, "blades.json");
        await new BladeStore(bladesPath).SaveAsync(CreateBlades());
        var blades = JsonNode.Parse(await File.ReadAllTextAsync(bladesPath))!;
        blades["blades"]![1]!["name"] = "fine CROSSCUT";
        await File.WriteAllTextAsync(bladesPath, blades.ToJsonString());
        error = await Assert.ThrowsAsync<InvalidDataException>(() => new BladeStore(bladesPath).LoadAsync());
        Assert.Contains("more than once", error.Message);
    }

    private static Inventory CreateInventory()
    {
        var inventory = new Inventory();
        inventory.Panels.Add(new Panel(2400, 1200, TestMaterials.Id("Oak", 18), 2)
        {
            Label = "Panel A", Priority = 3, TrimTop = 5, TrimBottom = 10, TrimLeft = 15, TrimRight = 20, CostPerUnit = 125.45m
        });
        inventory.Scraps.Add(new Scrap(400, 300, TestMaterials.Id("Oak", 18), 0)
        {
            Label = "Offcut B", Priority = -2, CostPerUnit = 12.34m, IsEnabled = false
        });
        inventory.Scraps.Add(new Scrap(200, 200, TestMaterials.Id("Birch", 12)));
        return inventory;
    }

    private static BladeCatalogue CreateBlades()
    {
        var catalogue = new BladeCatalogue();
        var freud = new Brand("Freud");
        var festool = new Brand("Festool");
        catalogue.Brands.AddRange([freud, festool]);
        catalogue.Blades.Add(new Blade("Fine crosscut", 250, 80, 3.2, freud.Id, "LU3D 1000"));
        catalogue.Blades.Add(new Blade("Rip", 300, 24, 0, festool.Id));
        catalogue.Blades.Add(new Blade("Generic", 160, 12, 2.2));
        return catalogue;
    }

    private static Project CreateProject()
    {
        var project = new Project { Unit = LengthUnit.Inches, BladeId = TestMaterials.BladeId(3.2), CutPattern = CutPattern.ByWidth };
        project.Parts.Add(new Part(254, 127, TestMaterials.Id("Oak"), 3)
        {
            Label = "Shelf, left", Color = "#AABBCC"
        });
        project.Parts.Add(new Part(500, 300, TestMaterials.Id("Birch"))
        {
            Label = "Door", IsEnabled = false
        });
        return project;
    }

    private static async Task WriteJsonAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}