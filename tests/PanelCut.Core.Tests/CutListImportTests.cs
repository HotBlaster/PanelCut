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
            Label,Width,Height,Quantity,Material,Thickness,EdgeBandTop,EdgeBandLeft,GroupTag
            "Side, left",600,720.5,2,Birch,18,yes,x,Carcass
            Shelf,564,300,,Birch,12,,,
            """, Catalogue);

        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Parts.Count);
        var side = result.Parts[0];
        Assert.Equal("Side, left", side.Label);
        Assert.Equal(600, side.Width);
        Assert.Equal(720.5, side.Height);
        Assert.Equal(2, side.Quantity);
        Assert.Equal(TestMaterials.Id("Birch", 18), side.MaterialId);
        Assert.True(side.EdgeBandTop);
        Assert.True(side.EdgeBandLeft);
        Assert.False(side.EdgeBandBottom);
        Assert.Equal("Carcass", side.GroupTag);
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
    public void ExactCaseNamePreferredOverCaseInsensitiveMatch()
    {
        var result = CutListImporter.Parse("Width,Height,Material,Thickness\n100,100,oak,12\n100,100,OAK,12", Catalogue);

        Assert.Equal(TestMaterials.Id("oak", 12), Assert.Single(result.Parts).MaterialId);
        Assert.Contains("matches 2", Assert.Single(result.Skipped).Message);
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
            ambiguous,100,200,1,Birch,
            nothick,100,200,1,Birch,5
            extra,100,200,1,Birch,18,surplus
            "multi
            line",100,200,1,Birch,18
            """, Catalogue);

        Assert.Equal(["ok", "multi\nline"], result.Parts.Select(part => part.Label.ReplaceLineEndings("\n")));
        Assert.Equal([3, 4, 5, 6, 7, 8, 9], result.Skipped.Select(issue => issue.Line));
        Assert.Contains("Walnut", result.Skipped[3].Message);
        Assert.Contains("matches 3", result.Skipped[4].Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n  \r\n")]
    [InlineData("Width,Height\n100,200")]
    [InlineData("Width,Height,Material,Colour\n100,200,Birch,red")]
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
            await File.WriteAllTextAsync(path, "Width,Height,Material,Thickness\n100,200,Birch,1\n");
            var result = await CutListImporter.LoadAsync(path, Catalogue);
            Assert.Equal(TestMaterials.Id("Birch", 1), Assert.Single(result.Parts).MaterialId);
        }
        finally { File.Delete(path); }
    }
}
