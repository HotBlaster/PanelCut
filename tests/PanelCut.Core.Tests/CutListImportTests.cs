using PanelCut.Core.Models;
using PanelCut.Core.Persistence;

namespace PanelCut.Core.Tests;

public sealed class CutListImportTests
{
    private static readonly MaterialCatalogue Catalogue = TestMaterials.Catalogue();

    [Fact]
    public void CommaSeparatedRowsBecomePartsInMillimetres()
    {
        var result = CutListImporter.Parse("""
            Label,Width,Height,Quantity,Material,Thickness,Color
            "Side, left",600,720.5,2,Birch,18,#aabbcc
            Shelf,564,300,,Birch 12,12,
            """, Catalogue);

        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Parts.Count);
        var side = result.Parts[0];
        Assert.Equal("Side, left", side.Label);
        Assert.Equal(600, side.Width);
        Assert.Equal(720.5, side.Height);
        Assert.Equal(2, side.Quantity);
        Assert.Equal(TestMaterials.Id("Birch", 18), side.MaterialId);
        Assert.Equal("#AABBCC", side.Color);
        Assert.Equal(Part.DefaultColor, result.Parts[1].Color);
        Assert.Equal(1, result.Parts[1].Quantity);
        Assert.Equal(TestMaterials.Id("Birch", 12), result.Parts[1].MaterialId);
        Assert.NotEqual(result.Parts[0].Id, result.Parts[1].Id);
    }

    [Fact]
    public void SemicolonFilesAcceptDecimalCommaAndCaseInsensitiveHeaders()
    {
        var result = CutListImporter.Parse("\uFEFFwidth;HEIGHT;material;thickness\r\n600,5;300;birch;18\r\n\r\n", Catalogue);

        var part = Assert.Single(result.Parts);
        Assert.Equal(600.5, part.Width);
        Assert.Equal(TestMaterials.Id("Birch", 18), part.MaterialId);
    }

    [Fact]
    public void NameIdentifiesMaterialAndTypeThicknessColorAreChecked()
    {
        var result = CutListImporter.Parse("""
            Width,Height,Material,Type,Thickness,Color
            100,100,oak,plywood,18,
            100,100,Oak,Solid,,
            100,100,Oak,,12,
            100,100,Oak,,,red
            """, Catalogue);

        Assert.Equal(TestMaterials.Id("Oak"), Assert.Single(result.Parts).MaterialId);
        Assert.Equal([3, 4, 5], result.Skipped.Select(issue => issue.Line));
        Assert.Contains("type 'Plywood'", result.Skipped[0].Message);
        Assert.Contains("not 12 mm", result.Skipped[1].Message);
        Assert.Contains("#RRGGBB", result.Skipped[2].Message);
    }

    [Fact]
    public void InvalidRowsAreSkippedAndReportedWithLineNumbers()
    {
        var result = CutListImporter.Parse("""
            Label,Width,Height,Quantity,Material,Thickness
            ok,100,200,1,Birch,18
            zero,0,200,1,Birch,18
            text,abc,200,1,Birch,18
            qty,100,200,0,Birch,18
            unknown,100,200,1,Walnut,18
            wrongthick,100,200,1,Birch 12,18
            nothick,100,200,1,Birch,5
            extra,100,200,1,Birch,18,surplus
            "multi
            line",100,200,1,Birch,18
            """, Catalogue);

        Assert.Equal(["ok", "multi\nline"], result.Parts.Select(part => part.Label.ReplaceLineEndings("\n")));
        Assert.Equal([3, 4, 5, 6, 7, 8, 9], result.Skipped.Select(issue => issue.Line));
        Assert.Contains("Walnut", result.Skipped[3].Message);
        Assert.Contains("not 18 mm", result.Skipped[4].Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    [InlineData("Width,Height\n100,200")]
    [InlineData("Width,Height,Material,Colour\n100,200,Birch,red")]
    [InlineData("Width,Height,Material,EdgeBandTop\n100,200,Birch,yes")]
    [InlineData("Width,Height,Material,GroupTag\n100,200,Birch,Kitchen")]
    [InlineData("Width,Height,Material,width\n100,200,Birch,1")]
    [InlineData("Width,Height,Material\n\"100,200,Birch")]
    public void InvalidFilesAreRejected(string text) =>
        Assert.Throws<InvalidDataException>(() => CutListImporter.Parse(text, Catalogue));

    [Fact]
    public async Task LoadAsyncReadsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PanelCut.CutList.{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, "Width,Height,Material,Thickness\n100,200,Birch 1,1\n");
            var result = await CutListImporter.LoadAsync(path, Catalogue);
            Assert.Equal(TestMaterials.Id("Birch", 1), Assert.Single(result.Parts).MaterialId);
        }
        finally { File.Delete(path); }
    }
}
