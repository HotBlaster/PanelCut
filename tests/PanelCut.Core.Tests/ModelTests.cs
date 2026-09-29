using PanelCut.Core.Models;

namespace PanelCut.Core.Tests;

public class ModelTests
{
    [Fact]
    public void MaterialCatalogueValidatesAndResolvesEntries()
    {
        var material = new Material("Oak", "Plywood", 18);
        var catalogue = new MaterialCatalogue();
        catalogue.Materials.Add(material);
        catalogue.Validate();
        Assert.Same(material, catalogue.Resolve(material.Id));
        Assert.Throws<ArgumentException>(() => catalogue.Resolve(Guid.NewGuid()));
        catalogue.Materials.Add(material);
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Materials.RemoveAt(1);
        catalogue.Materials.Add(new Material(" oak ", "Solid", 12));
        Assert.Contains("more than once", Assert.Throws<ArgumentException>(catalogue.Validate).Message);
        catalogue.Materials.RemoveAt(1);
        Assert.Throws<ArgumentException>(() => new Material(" ", "Plywood", 18));
        Assert.Throws<ArgumentException>(() => new Material("Oak", " ", 18));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", double.NaN));
        Assert.Throws<ArgumentException>(() => new Material("Oak", "Plywood", 18) { Id = Guid.Empty });
    }

    [Fact]
    public void BladesValidateFieldsAndAllowZeroKerf()
    {
        var brand = new Brand("  Freud ");
        Assert.Equal("Freud", brand.Name);
        var blade = new Blade("Fine", 250, 80, 0, brand.Id, "LU3D");
        Assert.Equal(("Fine", 250d, 80, 0d, (Guid?)brand.Id, "LU3D"),
            (blade.Name, blade.Diameter, blade.Teeth, blade.Kerf, blade.BrandId, blade.BrandCode));
        Assert.Equal((null, ""), (new Blade("Rip", 300, 24, 3.2).BrandId, new Blade("Rip", 300, 24, 3.2).BrandCode));
        Assert.Throws<ArgumentException>(() => new Brand(" "));
        Assert.Throws<ArgumentException>(() => new Brand("Freud") { Id = Guid.Empty });
        Assert.Throws<ArgumentException>(() => new Blade(" ", 250, 80, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blade("Fine", 0, 80, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blade("Fine", double.NaN, 80, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blade("Fine", 250, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blade("Fine", 250, 80, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blade("Fine", 250, 80, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new Blade("Fine", 250, 80, 3, Guid.Empty));
        Assert.Throws<ArgumentNullException>(() => new Blade("Fine", 250, 80, 3, null, null!));
        Assert.Throws<ArgumentException>(() => blade with { Id = Guid.Empty });
    }

    [Fact]
    public void BladeCatalogueValidatesBrandsAndReferences()
    {
        var freud = new Brand("Freud");
        var blade = new Blade("Fine", 250, 80, 3, freud.Id);
        var catalogue = new BladeCatalogue();
        catalogue.Brands.Add(freud);
        catalogue.Blades.Add(blade);
        catalogue.Validate();
        Assert.Same(blade, catalogue.Resolve(blade.Id));
        Assert.Same(freud, catalogue.ResolveBrand(freud.Id));
        Assert.Null(catalogue.ResolveBrand(null));
        Assert.True(catalogue.IsBrandInUse(freud.Id));
        Assert.Throws<ArgumentException>(() => catalogue.Resolve(Guid.NewGuid()));
        catalogue.Brands.Add(new Brand("freud"));
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Brands.RemoveAt(1);
        catalogue.Brands.Remove(freud);
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Brands.Add(freud);
        catalogue.Blades.Add(blade);
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Blades.RemoveAt(1);
        catalogue.Brands.Add(freud);
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Brands.RemoveAt(1);
        catalogue.Blades.Add(null!);
        Assert.Throws<ArgumentException>(catalogue.Validate);
        catalogue.Blades.RemoveAt(1);
        catalogue.Blades.Add(new Blade("FINE ", 300, 24, 3));
        Assert.Contains("Blade \"Fine\"", Assert.Throws<ArgumentException>(catalogue.Validate).Message);
        catalogue.Blades.RemoveAt(1);
        catalogue.Blades.Clear();
        Assert.False(catalogue.IsBrandInUse(freud.Id));
        catalogue.Validate();
    }

    [Fact]
    public void DefaultsAndCollectionsAreIndependent()
    {
        var project = new Project();
        var panel = new Panel(2400, 1200, TestMaterials.Id("Oak", 18));
        var scrap = new Scrap(400, 300, TestMaterials.Id("Oak", 18));
        var part = new Part(100, 200, TestMaterials.Id("Oak"));
        Assert.Equal(LengthUnit.Millimetres, project.Unit);
        Assert.Null(project.BladeId);
        Assert.Equal(0, panel.EdgeTrim);
        Assert.Equal(0, panel.Priority);
        Assert.Equal(0m, panel.CostPerUnit);
        Assert.Equal(1, panel.Quantity);
        Assert.Null(scrap.OriginPanelId);
        Assert.NotEqual(Guid.Empty, panel.Id);
        Assert.NotEqual(panel.Id, scrap.Id);
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.Equal(Part.DefaultColor, part.Color);
        project.Parts.Add(part);
        Assert.Empty(new Project().Parts);
        var inventory = new Inventory();
        inventory.Panels.Add(panel);
        Assert.Empty(new Inventory().Panels);
    }

    [Theory]
    [InlineData(0, 100, 80, true)]
    [InlineData(5, 90, 70, true)]
    [InlineData(40, 20, 0, false)]
    [InlineData(60, -20, -40, false)]
    public void TrimDeterminesUsableAreaForBothStockTypes(double trim, double width, double height, bool usable)
    {
        IStockItem[] stock = [new Panel(100, 80, TestMaterials.Id("Oak", 18)), new Scrap(100, 80, TestMaterials.Id("Oak", 18))];
        foreach (var item in stock)
        {
            item.EdgeTrim = trim;
            Assert.Equal(width, item.UsableWidth);
            Assert.Equal(height, item.UsableHeight);
            Assert.Equal(usable, item.IsUsable);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidLengthsAreRejectedWithoutChangingPreviousValues(double invalid)
    {
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18)) { EdgeTrim = 2 };
        var part = new Part(50, 40, TestMaterials.Id("Oak"));
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.EdgeTrim = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.Width = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.Height = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => part.Width = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => part.Height = invalid);
        Assert.Equal(2, panel.EdgeTrim);
        Assert.Equal(100, panel.Width);
        Assert.Equal(50, part.Width);
    }

    [Fact]
    public void DepletedStockAndNegativePriorityAreValid()
    {
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18), 0) { Priority = -5 };
        Assert.Equal(0, panel.Quantity);
        Assert.Equal(-5, panel.Priority);
    }

    [Fact]
    public void InvalidRecordsAndEditsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Panel(0, 80, TestMaterials.Id("Oak", 18)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Panel(100, 0, TestMaterials.Id("Oak", 18)));
        Assert.Throws<ArgumentException>(() => new Scrap(100, 80, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Part(0, 80, TestMaterials.Id("Oak")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Part(100, 80, TestMaterials.Id("Oak"), 0));
        Assert.Throws<ArgumentException>(() => new Part(100, 80, Guid.Empty));
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18));
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.Quantity = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.CostPerUnit = -0.01m);
        Assert.Throws<ArgumentException>(() => panel.Id = Guid.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Project().Unit = (LengthUnit)999);
        Assert.Throws<ArgumentException>(() => new Scrap(10, 10, TestMaterials.Id("Oak", 1)).OriginPanelId = Guid.Empty);
        Assert.Equal(TestMaterials.Id("Oak"), panel.MaterialId);
        Assert.Equal(string.Empty, panel.Label);
        Assert.Throws<ArgumentNullException>(() => panel.Label = null!);
        var part = new Part(100, 80, TestMaterials.Id("Oak")) { Color = "#a1b2c3" };
        Assert.Equal("#A1B2C3", part.Color);
        foreach (var invalid in new[] { "", "A1B2C3", "#A1B2C", "#A1B2C3D", "#GGGGGG", "red" })
            Assert.Throws<ArgumentException>(() => part.Color = invalid);
        Assert.Throws<ArgumentNullException>(() => part.Color = null!);
        Assert.Equal("#A1B2C3", part.Color);
    }

    [Fact]
    public void AggregateValidationRejectsDuplicatesAndNulls()
    {
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18));
        var inventory = new Inventory();
        inventory.Panels.Add(panel);
        inventory.Scraps.Add(new Scrap(20, 30, TestMaterials.Id("Oak", 18)) { Id = panel.Id });
        Assert.Throws<ArgumentException>(inventory.Validate);
        inventory.Scraps.Clear();
        inventory.Panels.Add(null!);
        Assert.Throws<ArgumentException>(inventory.Validate);
        var project = new Project();
        var part = new Part(10, 20, TestMaterials.Id("Oak"));
        project.Parts.AddRange([part, part]);
        Assert.Throws<ArgumentException>(project.Validate);
        project.Parts.Clear();
        project.Parts.Add(null!);
        Assert.Throws<ArgumentException>(project.Validate);
    }
}