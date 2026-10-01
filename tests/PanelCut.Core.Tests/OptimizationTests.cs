using System.Text.Json;
using PanelCut.Core.Models;
using PanelCut.Core.Optimization;

namespace PanelCut.Core.Tests;

public class OptimizationTests
{
    [Fact]
    public void CatalogueChangesAffectReferencesButNotEarlierResults()
    {
        var catalogue = TestMaterials.Catalogue();
        var blades = TestMaterials.Blades();
        var selected = catalogue.Resolve(TestMaterials.Id("Oak"));
        var inventory = Stock(new Panel(100, 100, selected.Id) { Label = "Stored label" });
        var project = Job(new Part(100, 100, selected.Id));
        var optimizer = new PanelOptimizer();
        var original = optimizer.OptimizePanels(inventory, project, catalogue, blades);
        Assert.True(original.IsComplete);
        catalogue.Materials.Remove(selected);
        catalogue.Materials.Add(new Material("Oak", "Solid", 18) { Id = selected.Id });
        var updated = optimizer.OptimizePanels(inventory, project, catalogue, blades);
        Assert.True(updated.IsComplete);
        Assert.Equal("Solid", updated.Sheets[0].Stock.MaterialType);
        Assert.Equal("Plywood", original.Sheets[0].Stock.MaterialType);
        Assert.Equal("Stored label", original.Sheets[0].Stock.Label);
        catalogue.Materials.RemoveAll(material => material.Id == selected.Id);
        Assert.Throws<ArgumentException>(() => optimizer.OptimizePanels(inventory, project, catalogue, blades));
    }

    [Fact]
    public void KerfComesFromSelectedBladeAndFollowsBladeEdits()
    {
        var blades = new BladeCatalogue();
        var blade = new Blade("Rip", 250, 24, 1);
        blades.Blades.Add(blade);
        var inventory = Stock(new Panel(101, 100, TestMaterials.Id("Oak", 18)));
        var project = Job(new Part(50, 100, TestMaterials.Id("Oak"), 2));
        project.BladeId = blade.Id;
        var optimizer = new PanelOptimizer();
        var result = optimizer.OptimizePanels(inventory, project, TestMaterials.Catalogue(), blades);
        Assert.Equal(new JobSettings(blade.Id, "Rip", 1, LengthUnit.Millimetres), result.Settings);
        Assert.Equal(2, Assert.Single(result.Sheets).Placements.Count);
        blades.Blades[0] = new Blade("Rip wide", 250, 24, 3) { Id = blade.Id };
        result = optimizer.OptimizePanels(inventory, project, TestMaterials.Catalogue(), blades);
        Assert.Equal(("Rip wide", 3d), (result.Settings.BladeName, result.Settings.KerfWidth));
        Assert.Single(Assert.Single(result.Sheets).Placements);
    }

    [Fact]
    public void MissingOrUnselectedBladeBlocksOptimization()
    {
        var optimizer = new PanelOptimizer();
        var inventory = Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18)));
        var project = new Project();
        project.Parts.Add(new Part(50, 50, TestMaterials.Id("Oak")));
        var error = Assert.Throws<ArgumentException>(() => optimizer.OptimizePanels(inventory, project, TestMaterials.Catalogue(), TestMaterials.Blades()));
        Assert.Contains("Select a blade", error.Message);
        project.BladeId = Guid.NewGuid();
        error = Assert.Throws<ArgumentException>(() => optimizer.OptimizePanels(inventory, project, TestMaterials.Catalogue(), TestMaterials.Blades()));
        Assert.Contains("missing", error.Message);
        Assert.Throws<ArgumentNullException>(() => optimizer.OptimizePanels(inventory, project, TestMaterials.Catalogue(), null!));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 1, 1)]
    public void KerfSeparatesPartsWithoutChargingOuterEdges(double kerf, int placed, int unplaced)
    {
        var inventory = new Inventory();
        inventory.Panels.Add(new Panel(100, 100, TestMaterials.Id("Oak", 18)));
        var project = TestMaterials.Project(kerf);
        project.Parts.Add(new Part(50, 100, TestMaterials.Id("Oak"), 2));

        var result = new TestOptimizer().OptimizePanels(inventory, project);

        Assert.Equal(placed, Assert.Single(result.Sheets).Placements.Count);
        Assert.Equal(unplaced, result.UnplacedParts.Sum(part => part.Quantity));
        Assert.Equal(unplaced == 0, result.IsComplete);
        Assert.Equal(1, inventory.Panels[0].Quantity);
        Assert.Equal(2, project.Parts[0].Quantity);
        if (kerf == 0)
            Assert.Equal(0, result.WastePercentage);
        AssertGeometry(result);
    }

    [Theory]
    [InlineData(101, 1, 2)]
    [InlineData(100.5, 1, 1)]
    [InlineData(100, 1000, 1)]
    public void KerfFitsExactlyOrDiscardsNarrowRemainders(double width, double kerf, int count)
    {
        var inventory = Stock(new Panel(width, 100, TestMaterials.Id("Oak", 18)));
        var project = Job(new Part(50, 100, TestMaterials.Id("Oak"), 2), kerf);
        var result = new TestOptimizer().OptimizePanels(inventory, project);
        Assert.Equal(count, Assert.Single(result.Sheets).Placements.Count);
        AssertGeometry(result);
    }

    [Fact]
    public void ExactSheetFitRequiresNoCutsEvenWithLargeKerf()
    {
        var result = new TestOptimizer().OptimizePanels(Stock(new Panel(100, 80, TestMaterials.Id("Oak", 18))),
            Job(new Part(100, 80, TestMaterials.Id("Oak")), 1000));
        Assert.True(result.IsComplete);
        var sheet = Assert.Single(result.Sheets);
        Assert.Empty(sheet.Cuts);
        Assert.Empty(sheet.RemainingScraps);
        Assert.Equal(0, result.WasteArea);
        AssertGeometry(result);
    }

    [Theory]
    [InlineData(40, 70, CutAxis.Vertical)]
    [InlineData(70, 40, CutAxis.Horizontal)]
    public void RecordsBothGuillotineSplitOrders(double width, double height, CutAxis firstAxis)
    {
        var result = new TestOptimizer().OptimizePanels(Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18))),
            Job(new Part(width, height, TestMaterials.Id("Oak")), 2));
        Assert.Equal(firstAxis, Assert.Single(result.Sheets).Cuts[0].Axis);
        AssertGeometry(result);
    }

    [Theory]
    [InlineData(CutPattern.ByLength, 200, 100, CutAxis.Horizontal)]
    [InlineData(CutPattern.ByLength, 100, 200, CutAxis.Vertical)]
    [InlineData(CutPattern.ByWidth, 200, 100, CutAxis.Vertical)]
    [InlineData(CutPattern.ByWidth, 100, 200, CutAxis.Horizontal)]
    [InlineData(CutPattern.StripsByLength, 200, 100, CutAxis.Horizontal)]
    [InlineData(CutPattern.StripsByLength, 100, 200, CutAxis.Vertical)]
    [InlineData(CutPattern.StripsByWidth, 200, 100, CutAxis.Vertical)]
    [InlineData(CutPattern.StripsByWidth, 100, 200, CutAxis.Horizontal)]
    public void StripPatternsStartEveryPlacementWithTheSameAxis(CutPattern pattern, double width, double height, CutAxis axis)
    {
        var project = Job(new Part(30, 20, TestMaterials.Id("Oak"), 12), 1);
        project.Parts.Add(new Part(45, 15, TestMaterials.Id("Oak"), 5));
        project.CutPattern = pattern;
        var result = new TestOptimizer().OptimizePanels(Stock(new Panel(width, height, TestMaterials.Id("Oak", 18))), project);
        var sheet = Assert.Single(result.Sheets);
        Assert.Equal(axis, sheet.Cuts[0].Axis);
        // No cross cut may run through the whole sheet: strips are only subdivided.
        Assert.All(sheet.Cuts.Where(cut => cut.Axis != axis),
            cut => Assert.True(axis == CutAxis.Horizontal ? cut.Region.Height < height : cut.Region.Width < width));
        AssertGeometry(result);
    }

    [Fact]
    public void OptimalPatternIsTheDefault()
    {
        Assert.Equal(CutPattern.Optimal, new Project().CutPattern);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Project().CutPattern = (CutPattern)99);
    }

    [Fact]
    public void OptimalSearchIsAtLeastAsGoodAsEveryRestrictedPattern()
    {
        foreach (var seed in new[] { 3, 17, 42 })
        {
            var optimal = MixedJob(seed, CutPattern.Optimal);
            foreach (var pattern in Enum.GetValues<CutPattern>())
            {
                var other = MixedJob(seed, pattern);
                AssertGeometry(other);
                var (best, sheet) = (optimal.Sheets[0], other.Sheets[0]);
                Assert.True(best.PartArea >= sheet.PartArea);
                if (best.PartArea == sheet.PartArea && pattern != CutPattern.FewestCuts)
                    Assert.True(LargestScrap(best) >= LargestScrap(sheet));
            }
        }
    }

    [Fact]
    public void FewestCutsNeverCutsMoreThanOptimalForTheSameParts()
    {
        foreach (var seed in new[] { 3, 17, 42 })
        {
            var optimal = MixedJob(seed, CutPattern.Optimal).Sheets[0];
            var fewest = MixedJob(seed, CutPattern.FewestCuts).Sheets[0];
            Assert.Equal(optimal.PartArea, fewest.PartArea);
            Assert.True(fewest.Cuts.Count <= optimal.Cuts.Count);
        }
    }

    [Fact]
    public void StripsGroupEqualLengthPartsAndKeepOneFullOffcut()
    {
        var project = Job(new Part(500, 80, TestMaterials.Id("Oak"), 3));
        project.Parts.Add(new Part(300, 60, TestMaterials.Id("Oak"), 2));
        project.CutPattern = CutPattern.StripsByWidth;
        var result = new TestOptimizer().OptimizePanels(Stock(new Panel(1000, 600, TestMaterials.Id("Oak", 18))), project);
        var sheet = Assert.Single(result.Sheets);
        Assert.True(result.IsComplete);
        var longParts = sheet.Placements.Where(placement => placement.Part.Width == 500).ToArray();
        Assert.Single(longParts.Select(placement => placement.Bounds.Y).Distinct());
        Assert.Contains(sheet.RemainingScraps, scrap => scrap.Height == 600 && scrap.Right == 1000);
        AssertGeometry(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartsRotateToFitBothStockKinds(bool useScrap)
    {
        IStockItem item = useScrap ? new Scrap(80, 40, TestMaterials.Id("Oak", 18)) : new Panel(80, 40, TestMaterials.Id("Oak", 18));
        var project = Job(new Part(40, 80, TestMaterials.Id("Oak")));
        var result = new TestOptimizer().OptimizePanels(Stock(item), project);
        Assert.True(result.IsComplete);
        var placement = Assert.Single(Assert.Single(result.Sheets).Placements);
        Assert.True(placement.IsRotated);
        Assert.Equal(80, placement.Bounds.Width);
        Assert.Equal(40, placement.Bounds.Height);
        AssertGeometry(result);
    }

    [Fact]
    public void PanelEdgeTrimsOffsetPlacementsAndReduceUsableArea()
    {
        var panel = new Panel(120, 100, TestMaterials.Id("Oak", 18)) { TrimTop = 5, TrimBottom = 15, TrimLeft = 12, TrimRight = 8 };
        var part = new Part(100, 80, TestMaterials.Id("Oak"));
        var optimizer = new TestOptimizer();
        var result = optimizer.OptimizePanels(Stock(panel), Job(part));
        var sheet = Assert.Single(result.Sheets);
        Assert.Equal((5d, 15d, 12d, 8d), (sheet.Stock.TrimTop, sheet.Stock.TrimBottom, sheet.Stock.TrimLeft, sheet.Stock.TrimRight));
        Assert.Equal(new LayoutRectangle(12, 5, 100, 80), Assert.Single(sheet.Placements).Bounds);
        AssertGeometry(result);
        part.Width = 101;
        Assert.Empty(optimizer.OptimizePanels(Stock(panel), Job(part)).Sheets);
        part.Width = 100;
        part.Height = 81;
        Assert.Empty(optimizer.OptimizePanels(Stock(panel), Job(part)).Sheets);
    }

    [Fact]
    public void ScrapsHaveNoTrim()
    {
        var scrap = new Scrap(120, 100, TestMaterials.Id("Oak", 18));
        var result = new TestOptimizer().OptimizePanels(Stock(scrap), Job(new Part(120, 100, TestMaterials.Id("Oak"))));
        var sheet = Assert.Single(result.Sheets);
        Assert.Equal((0d, 0d, 0d, 0d), (sheet.Stock.TrimTop, sheet.Stock.TrimBottom, sheet.Stock.TrimLeft, sheet.Stock.TrimRight));
        Assert.Equal(new LayoutRectangle(0, 0, 120, 100), Assert.Single(sheet.Placements).Bounds);
        AssertGeometry(result);
    }

    [Theory]
    [InlineData(80, 120, 40)]
    [InlineData(120, 80, 40)]
    [InlineData(80, 80, 50)]
    public void FullyTrimmedPanelsAreSkipped(double width, double height, double trim)
    {
        var inventory = Stock(new Panel(width, height, TestMaterials.Id("Oak", 18))
            {
                TrimTop = trim, TrimBottom = trim, TrimLeft = trim, TrimRight = trim, Priority = -10
            },
            new Panel(100, 100, TestMaterials.Id("Oak", 18)) { Priority = 1 });
        var result = new TestOptimizer().OptimizePanels(inventory, Job(new Part(10, 10, TestMaterials.Id("Oak"))));
        Assert.Equal(inventory.Panels[1].Id, Assert.Single(result.Sheets).Stock.Id);
        AssertGeometry(result);
    }

    [Fact]
    public void PriorityCombinesStockKindsAndHonorsAvailableQuantities()
    {
        var early = new Scrap(100, 100, TestMaterials.Id("Oak", 18)) { Priority = -3, CostPerUnit = 1m };
        var middle = new Panel(100, 100, TestMaterials.Id("Oak", 18), 2) { Priority = 0, CostPerUnit = 5m };
        var late = new Panel(200, 100, TestMaterials.Id("Oak", 18), 2) { Priority = 10, CostPerUnit = 9m };
        var inventory = Stock(late, middle, early);
        var result = new TestOptimizer().OptimizePanels(inventory, Job(new Part(100, 100, TestMaterials.Id("Oak"), 5)));
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { early.Id, middle.Id, middle.Id, late.Id }, result.Sheets.Select(sheet => sheet.Stock.Id));
        Assert.Equal(new[] { 1, 1, 2, 1 }, result.Sheets.Select(sheet => sheet.UnitIndex));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.Sheets.SelectMany(sheet => sheet.Placements).Select(part => part.CopyIndex));
        Assert.Equal(4, result.StockItemsUsed);
        Assert.Equal(20m, result.TotalCost);
        Assert.Equal(0, result.WastePercentage);
        AssertGeometry(result);
    }

    [Fact]
    public void EqualPriorityUsesPanelThenScrapInputOrder()
    {
        var first = new Panel(100, 100, TestMaterials.Id("Oak", 18));
        var second = new Panel(100, 100, TestMaterials.Id("Oak", 18));
        var scrap = new Scrap(100, 100, TestMaterials.Id("Oak", 18));
        var result = new TestOptimizer().OptimizePanels(Stock(first, second, scrap), Job(new Part(100, 100, TestMaterials.Id("Oak"), 3)));
        Assert.Equal(new[] { first.Id, second.Id, scrap.Id }, result.Sheets.Select(sheet => sheet.Stock.Id));
    }

    [Fact]
    public void SmallerPartsFillCurrentSheetBeforeOpeningAnother()
    {
        var large = new Part(60, 60, TestMaterials.Id("Oak"), 2);
        var small = new Part(40, 60, TestMaterials.Id("Oak"));
        var project = Job(small);
        project.Parts.Add(large);
        var result = new TestOptimizer().OptimizePanels(Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18), 2)), project);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Sheets.Count);
        Assert.Equal(new[] { large.Id, small.Id }, result.Sheets[0].Placements.Select(part => part.Part.Id));
        Assert.Equal(large.Id, Assert.Single(result.Sheets[1].Placements).Part.Id);
        AssertGeometry(result);
    }

    [Fact]
    public void ImpossibleDemandDoesNotPreventSmallerPartsAndDoesNotOpenEmptySheets()
    {
        var inventory = Stock(new Panel(1, 1, TestMaterials.Id("Oak", 18), int.MaxValue) { Priority = -1, CostPerUnit = 500m },
            new Panel(100, 100, TestMaterials.Id("Oak", 18), int.MaxValue) { CostPerUnit = 10m });
        var project = Job(new Part(500, 500, TestMaterials.Id("Oak"), int.MaxValue));
        var smaller = new Part(100, 100, TestMaterials.Id("Oak"));
        project.Parts.Add(smaller);
        var result = new TestOptimizer().OptimizePanels(inventory, project);
        Assert.Equal(smaller.Id, Assert.Single(Assert.Single(result.Sheets).Placements).Part.Id);
        Assert.Equal(int.MaxValue, Assert.Single(result.UnplacedParts).Quantity);
        Assert.Equal(10m, result.TotalCost);
    }

    [Fact]
    public void MaterialMatchingIsExactAndDepletedStockIsIgnored()
    {
        var inventory = Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18), 0) { Priority = -3 },
            new Panel(100, 100, TestMaterials.Id("Pine", 18)) { Priority = -2 },
            new Scrap(100, 100, TestMaterials.Id("Birch", 18)) { Priority = -1 },
            new Panel(100, 100, TestMaterials.Id("Oak", 12)));
        var project = Job(new Part(100, 100, TestMaterials.Id("Oak"), 2));
        project.Parts.Add(new Part(100, 100, TestMaterials.Id("Birch")));
        var result = new TestOptimizer().OptimizePanels(inventory, project);
        Assert.Equal(new[] { "Birch" }, result.Sheets.Select(sheet => sheet.Stock.Material));
        Assert.Equal(2, Assert.Single(result.UnplacedParts).Quantity);
        AssertGeometry(result);
    }

    [Fact]
    public void WasteIncludesTrimKerfAndRemainingScrapOnOpenedSheetsOnly()
    {
        var stock = new Panel(120, 100, TestMaterials.Id("Oak", 18), 10)
        {
            TrimTop = 10, TrimBottom = 10, TrimLeft = 10, TrimRight = 10, CostPerUnit = 12.34m
        };
        var result = new TestOptimizer().OptimizePanels(Stock(stock), Job(new Part(50, 80, TestMaterials.Id("Oak")), 2));
        var sheet = Assert.Single(result.Sheets);
        Assert.Equal(12000, result.TotalStockArea);
        Assert.Equal(4000, result.TotalPartArea);
        Assert.Equal(8000, result.WasteArea);
        Assert.Equal(100.0 * 2 / 3, result.WastePercentage, 10);
        Assert.Equal(12.34m, result.TotalCost);
        Assert.Equal(3840, Assert.Single(sheet.RemainingScraps).Area);
        Assert.Equal(2, Assert.Single(sheet.Cuts).KerfWidth);
        AssertGeometry(result);
    }

    [Fact]
    public void EmptyJobsAndMissingStockHaveWellDefinedResults()
    {
        var optimizer = new TestOptimizer();
        var result = optimizer.OptimizePanels(Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18))), TestMaterials.Project());
        Assert.True(result.IsComplete);
        Assert.Empty(result.Sheets);
        Assert.Equal(0, result.WastePercentage);
        Assert.Equal(0, result.TotalStockArea);
        Assert.Equal(0, result.TotalPartArea);
        Assert.Equal(0m, result.TotalCost);
        var project = Job(new Part(50, 50, TestMaterials.Id("Oak"), 2));
        project.Parts.Add(new Part(20, 30, TestMaterials.Id("Birch"), 3));
        result = optimizer.OptimizePanels(new Inventory(), project);
        Assert.False(result.IsComplete);
        Assert.Equal(project.Parts.Select(part => part.Id), result.UnplacedParts.Select(part => part.Part.Id));
        Assert.Equal(new[] { 2, 3 }, result.UnplacedParts.Select(part => part.Quantity));
        Assert.Equal(0, result.WastePercentage);
    }

    [Fact]
    public void OptimizeNeverMutatesInputsAndResultsRemainDetached()
    {
        var stock = new Scrap(80, 40, TestMaterials.Id("Oak", 18), 2)
        {
            Priority = -2, CostPerUnit = 10m
        };
        var part = new Part(40, 80, TestMaterials.Id("Oak"), 3)
        {
            Label = "Shelf", Color = "#123456"
        };
        var inventory = Stock(stock);
        var project = Job(part);
        project.Unit = LengthUnit.Inches;
        var beforeStock = JsonSerializer.Serialize(inventory);
        var beforeJob = JsonSerializer.Serialize(project);
        var result = new TestOptimizer().OptimizePanels(inventory, project);
        Assert.Equal(beforeStock, JsonSerializer.Serialize(inventory));
        Assert.Equal(beforeJob, JsonSerializer.Serialize(project));
        var snapshot = JsonSerializer.Serialize(result);
        var placed = result.Sheets[0].Placements[0];
        Assert.True(placed.IsRotated);
        Assert.Equal(40, placed.Part.Width);
        Assert.Equal(80, placed.Part.Height);
        Assert.Equal("#123456", placed.Part.Color);
        stock.Quantity = 0;
        stock.MaterialId = TestMaterials.Id("Birch");
        part.Label = "Changed";
        part.Width = 30;
        inventory.Scraps.Clear();
        project.Parts.Clear();
        project.BladeId = TestMaterials.BladeId(8);
        Assert.Equal(snapshot, JsonSerializer.Serialize(result));
        Assert.Throws<NotSupportedException>(() => ((IList<SheetLayout>)result.Sheets).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PlacedPart>)result.Sheets[0].Placements).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LayoutRectangle>)result.Sheets[0].RemainingScraps).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<GuillotineCut>)result.Sheets[0].Cuts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<UnplacedPart>)result.UnplacedParts).Clear());
    }

    [Fact]
    public void RepeatedRunsAreDeterministicAndUnitDoesNotChangeGeometry()
    {
        var optimizer = new TestOptimizer();
        var inventory = Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18), 2));
        var project = Job(new Part(30, 50, TestMaterials.Id("Oak"), 5), 0.5);
        var first = optimizer.OptimizePanels(inventory, project);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(optimizer.OptimizePanels(inventory, project)));
        project.Unit = LengthUnit.Inches;
        var inches = optimizer.OptimizePanels(inventory, project);
        Assert.Equal(JsonSerializer.Serialize(first.Sheets), JsonSerializer.Serialize(inches.Sheets));
        Assert.Equal(LengthUnit.Inches, inches.Settings.Unit);
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        var optimizer = new TestOptimizer();
        Assert.Throws<ArgumentNullException>(() => optimizer.OptimizePanels(null!, TestMaterials.Project()));
        Assert.Throws<ArgumentNullException>(() => optimizer.OptimizePanels(new Inventory(), null!));
        var project = TestMaterials.Project();
        Assert.Throws<ArgumentException>(() => project.BladeId = Guid.Empty);
        Assert.Equal(TestMaterials.BladeId(0), project.BladeId);
        var inventory = Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18)));
        inventory.Panels.Add(inventory.Panels[0]);
        Assert.Throws<ArgumentException>(() => optimizer.OptimizePanels(inventory, project));
        project.Parts.Add(null!);
        Assert.Throws<ArgumentException>(() => optimizer.OptimizePanels(new Inventory(), project));
    }

    [Fact]
    public void RejectsUnrepresentableAreasAndCostsWithoutChangingInputs()
    {
        var optimizer = new TestOptimizer();
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.OptimizePanels(new Inventory(), Job(new Part(double.MaxValue, 2, TestMaterials.Id("Oak")))));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.OptimizePanels(new Inventory(), Job(new Part(double.Epsilon, double.Epsilon, TestMaterials.Id("Oak")))));
        var inventory = Stock(new Panel(100, 100, TestMaterials.Id("Oak", 18), 2) { CostPerUnit = decimal.MaxValue });
        var project = Job(new Part(100, 100, TestMaterials.Id("Oak"), 2));
        var beforeStock = JsonSerializer.Serialize(inventory);
        var beforeProject = JsonSerializer.Serialize(project);
        Assert.Throws<OverflowException>(() => optimizer.OptimizePanels(inventory, project));
        Assert.Equal(beforeStock, JsonSerializer.Serialize(inventory));
        Assert.Equal(beforeProject, JsonSerializer.Serialize(project));
        inventory = Stock(new Panel(1e154, 1e154, TestMaterials.Id("Oak", 18), 2));
        Assert.Throws<OverflowException>(() => optimizer.OptimizePanels(inventory, Job(new Part(1e154, 1e154, TestMaterials.Id("Oak"), 2))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(3.2)]
    public void SeededMixedJobsPreserveGeometryAndDemand(double kerf)
    {
        var random = new Random(731);
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var inventory = Stock(new Panel(200, 150, TestMaterials.Id("Oak", 18), 3) { TrimTop = 5, TrimBottom = 3, TrimLeft = 7, TrimRight = 2 },
                new Scrap(120, 90, TestMaterials.Id("Oak", 18), 2) { Priority = -1 },
                new Panel(200, 150, TestMaterials.Id("Birch", 18), 2));
            var project = TestMaterials.Project(kerf);
            for (var index = 0; index < 18; index++)
                project.Parts.Add(new Part(random.Next(15, 110) + 0.25, random.Next(15, 110) + 0.5,
                    TestMaterials.Id(index % 3 == 0 ? "Birch" : "Oak"), random.Next(1, 4)));
            var result = new TestOptimizer().OptimizePanels(inventory, project);
            AssertGeometry(result);
            var placements = result.Sheets.SelectMany(sheet => sheet.Placements).ToArray();
            foreach (var part in project.Parts)
            {
                var copies = placements.Where(placement => placement.Part.Id == part.Id).ToArray();
                var unplaced = result.UnplacedParts.Where(item => item.Part.Id == part.Id).Sum(item => item.Quantity);
                Assert.Equal(part.Quantity, copies.Length + unplaced);
                Assert.Equal(Enumerable.Range(1, copies.Length), copies.Select(copy => copy.CopyIndex));
            }
            foreach (var used in result.Sheets.GroupBy(sheet => sheet.Stock.Id))
                Assert.True(used.Count() <= used.First().Stock.Quantity);
            Assert.Equal(result.Sheets.Select(sheet => sheet.Stock.Priority).Order(), result.Sheets.Select(sheet => sheet.Stock.Priority));
        }
    }

    private static OptimizationResult MixedJob(int seed, CutPattern pattern)
    {
        var random = new Random(seed);
        var project = TestMaterials.Project(3.2);
        project.CutPattern = pattern;
        for (var index = 0; index < 10; index++)
            project.Parts.Add(new Part(random.Next(50, 600), random.Next(30, 300), TestMaterials.Id("Oak"), random.Next(1, 4)));
        return new TestOptimizer().OptimizePanels(Stock(new Panel(1525, 1525, TestMaterials.Id("Oak", 18), 5)), project);
    }

    private static double LargestScrap(SheetLayout sheet) =>
        sheet.RemainingScraps.Count == 0 ? 0 : sheet.RemainingScraps.Max(scrap => scrap.Area);

    private static Inventory Stock(params IStockItem[] items)
    {
        var inventory = new Inventory();
        foreach (var item in items)
        {
            if (item is Panel panel)
                inventory.Panels.Add(panel);
            else
                inventory.Scraps.Add((Scrap)item);
        }
        return inventory;
    }

    private static Project Job(Part part, double kerf = 0)
    {
        var project = TestMaterials.Project(kerf);
        project.Parts.Add(part);
        return project;
    }

    private static void AssertGeometry(OptimizationResult result)
    {
        const double tolerance = 1e-7;
        foreach (var sheet in result.Sheets)
        {
            var stock = sheet.Stock;
            var leaves = new List<LayoutRectangle> { new(stock.TrimLeft, stock.TrimTop, stock.UsableWidth, stock.UsableHeight) };
            var kerfArea = 0.0;
            foreach (var cut in sheet.Cuts)
            {
                var leaf = Assert.Single(leaves, leaf => SameRectangle(leaf, cut.Region));
                leaves.Remove(leaf);
                var horizontal = cut.Axis == CutAxis.Horizontal;
                var length = cut.Position - (horizontal ? leaf.Y : leaf.X);
                var available = horizontal ? leaf.Height : leaf.Width;
                Assert.True(length > 0 && length < available);
                Assert.Equal(Math.Min(result.Settings.KerfWidth, available - length), cut.KerfWidth, 7);
                kerfArea += cut.KerfWidth * (horizontal ? leaf.Width : leaf.Height);
                leaves.Add(horizontal ? new(leaf.X, leaf.Y, leaf.Width, length) : new(leaf.X, leaf.Y, length, leaf.Height));
                var remainder = available - length - cut.KerfWidth;
                if (remainder > tolerance)
                    leaves.Add(horizontal ? new(leaf.X, cut.Position + cut.KerfWidth, leaf.Width, remainder)
                        : new(cut.Position + cut.KerfWidth, leaf.Y, remainder, leaf.Height));
            }
            foreach (var placement in sheet.Placements)
            {
                var rectangle = placement.Bounds;
                Assert.Equal(stock.Material, placement.Part.Material);
                Assert.Equal(stock.MaterialType, placement.Part.MaterialType);
                Assert.Equal(stock.Thickness, placement.Part.Thickness);
                Assert.Equal(placement.IsRotated ? placement.Part.Height : placement.Part.Width, rectangle.Width);
                Assert.Equal(placement.IsRotated ? placement.Part.Width : placement.Part.Height, rectangle.Height);
                Assert.True(rectangle.X >= stock.TrimLeft - tolerance && rectangle.Y >= stock.TrimTop - tolerance);
                Assert.True(rectangle.Right <= stock.Width - stock.TrimRight + tolerance);
                Assert.True(rectangle.Bottom <= stock.Height - stock.TrimBottom + tolerance);
                var leaf = Assert.Single(leaves, leaf => SameRectangle(leaf, rectangle));
                leaves.Remove(leaf);
            }
            Assert.Equal(sheet.RemainingScraps.Count, leaves.Count);
            foreach (var free in sheet.RemainingScraps)
                Assert.Single(leaves, leaf => SameRectangle(leaf, free));
            for (var first = 0; first < sheet.Placements.Count; first++)
            for (var second = first + 1; second < sheet.Placements.Count; second++)
            {
                var left = sheet.Placements[first].Bounds;
                var right = sheet.Placements[second].Bounds;
                Assert.True(left.Right + result.Settings.KerfWidth <= right.X + tolerance
                    || right.Right + result.Settings.KerfWidth <= left.X + tolerance
                    || left.Bottom + result.Settings.KerfWidth <= right.Y + tolerance
                    || right.Bottom + result.Settings.KerfWidth <= left.Y + tolerance);
            }
            Assert.Equal(stock.UsableWidth * stock.UsableHeight,
                sheet.PartArea + sheet.RemainingScraps.Sum(rectangle => rectangle.Area) + kerfArea, 6);
        }
    }

    private static bool SameRectangle(LayoutRectangle first, LayoutRectangle second) =>
        Math.Abs(first.X - second.X) < 1e-7 && Math.Abs(first.Y - second.Y) < 1e-7
        && Math.Abs(first.Width - second.Width) < 1e-7 && Math.Abs(first.Height - second.Height) < 1e-7;
}