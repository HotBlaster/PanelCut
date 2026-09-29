using PanelCut.Core.Models;

namespace PanelCut.Core.Tests;

public class UnitConversionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12.5)]
    public void InchesRoundTrip(double inches)
    {
        var millimetres = UnitConversion.ToMillimetres(inches, LengthUnit.Inches);
        Assert.Equal(inches * 25.4, millimetres, 10);
        Assert.Equal(inches, UnitConversion.FromMillimetres(millimetres, LengthUnit.Inches), 10);
        Assert.Equal(millimetres, UnitConversion.ToMillimetres(millimetres, LengthUnit.Millimetres));
    }

    [Fact]
    public void UnitToggleDoesNotChangePhysicalDimensions()
    {
        var project = TestMaterials.Project(3.2);
        project.Parts.Add(new Part(254, 127, TestMaterials.Id("Oak")));
        project.Unit = LengthUnit.Inches;
        Assert.Equal(254, project.Parts[0].Width);
        Assert.Equal(TestMaterials.BladeId(3.2), project.BladeId);
        Assert.Equal(10, UnitConversion.FromMillimetres(project.Parts[0].Width, project.Unit));
    }

    [Fact]
    public void InvalidOrOverflowingConversionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.ToMillimetres(double.MaxValue, LengthUnit.Inches));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.ToMillimetres(-1, LengthUnit.Inches));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromMillimetres(double.NaN, LengthUnit.Millimetres));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitConversion.FromMillimetres(1, (LengthUnit)999));
    }
}