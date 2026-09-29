namespace PanelCut.Core.Models;

public static class UnitConversion
{
    public static double ToMillimetres(double value, LengthUnit unit)
    {
        Validation.NonNegative(value, nameof(value));
        Validation.Defined(unit);
        return Validation.NonNegative(unit == LengthUnit.Inches ? value * 25.4 : value, nameof(value));
    }

    public static double FromMillimetres(double value, LengthUnit unit)
    {
        Validation.NonNegative(value, nameof(value));
        Validation.Defined(unit);
        return unit == LengthUnit.Inches ? value / 25.4 : value;
    }
}