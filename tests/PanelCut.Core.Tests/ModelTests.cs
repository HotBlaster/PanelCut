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
        Assert.Throws<ArgumentException>(() => new Material(" ", "Plywood", 18));
        Assert.Throws<ArgumentException>(() => new Material("Oak", " ", 18));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", double.NaN));
        Assert.Throws<ArgumentException>(() => new Material("Oak", "Plywood", 18) { Id = Guid.Empty });
    }

    [Fact]
    public void DefaultsAndCollectionsAreIndependent()
    {
        var project = new Project();
        var panel = new Panel(2400, 1200, TestMaterials.Id("Oak", 18));
        var scrap = new Scrap(400, 300, TestMaterials.Id("Oak", 18));
        var part = new Part(100, 200, TestMaterials.Id("Oak"));
        Assert.Equal(LengthUnit.Millimetres, project.Unit);
        Assert.Equal(0, project.KerfWidth);
        Assert.Equal(0, panel.EdgeTrim);
        Assert.Equal(0, panel.Priority);
        Assert.Equal(0m, panel.CostPerUnit);
        Assert.Equal(1, panel.Quantity);
        Assert.Null(scrap.OriginPanelId);
        Assert.NotEqual(Guid.Empty, panel.Id);
        Assert.NotEqual(panel.Id, scrap.Id);
        Assert.NotEqual(Guid.Empty, part.Id);
        Assert.False(part.EdgeBandTop || part.EdgeBandBottom || part.EdgeBandLeft || part.EdgeBandRight);
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
        var project = new Project { KerfWidth = 3 };
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18)) { EdgeTrim = 2 };
        var part = new Part(50, 40, TestMaterials.Id("Oak"));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.KerfWidth = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.EdgeTrim = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.Width = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => panel.Height = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Material("Oak", "Plywood", invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => part.Width = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => part.Height = invalid);
        Assert.Equal(3, project.KerfWidth);
        Assert.Equal(2, panel.EdgeTrim);
        Assert.Equal(100, panel.Width);
        Assert.Equal(50, part.Width);
    }

    [Fact]
    public void ZeroKerfDepletedStockAndNegativePriorityAreValid()
    {
        var project = new Project { KerfWidth = 0 };
        var panel = new Panel(100, 80, TestMaterials.Id("Oak", 18), 0) { Priority = -5 };
        Assert.Equal(0, project.KerfWidth);
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