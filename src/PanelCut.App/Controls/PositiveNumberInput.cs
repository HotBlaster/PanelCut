using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PanelCut.App.Presentation;

namespace PanelCut.App.Controls;

public sealed class PositiveNumberInput : TextBox
{
    public static RoutedUICommand Increase { get; } = new("Increase thickness", nameof(Increase), typeof(PositiveNumberInput));
    public static RoutedUICommand Decrease { get; } = new("Decrease thickness", nameof(Decrease), typeof(PositiveNumberInput));

    public PositiveNumberInput()
    {
        InputMethod.SetIsInputMethodEnabled(this, false);
        AllowDrop = false;
        CommandBindings.Add(new CommandBinding(Increase, (_, _) => Step(1), (_, args) => args.CanExecute = CanStep(1)));
        CommandBindings.Add(new CommandBinding(Decrease, (_, _) => Step(-1), (_, args) => args.CanExecute = CanStep(-1)));
        DataObject.AddPastingHandler(this, OnPaste);
    }

    private static bool TryPositive(string text, out double value) =>
        double.TryParse(EditableRow.NormalizeDecimal(text), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value)
        && double.IsFinite(value) && value > 0;

    private string ReplaceSelection(string replacement) =>
        Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, replacement);

    protected override void OnPreviewTextInput(TextCompositionEventArgs args)
    {
        var candidate = EditableRow.NormalizeDecimal(ReplaceSelection(args.Text));
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var digits = candidate.Replace(separator, "", StringComparison.Ordinal);
        var firstSeparator = candidate.IndexOf(separator, StringComparison.Ordinal);
        args.Handled = !digits.All(char.IsAsciiDigit)
            || (firstSeparator >= 0 && candidate.IndexOf(separator, firstSeparator + separator.Length, StringComparison.Ordinal) >= 0);
        base.OnPreviewTextInput(args);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs args)
    {
        if (args.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted
            || !TryPositive(ReplaceSelection(pasted), out _))
            args.CancelCommand();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        if (args.Key is Key.Up or Key.Down)
        {
            Step(args.Key == Key.Up ? 1 : -1);
            args.Handled = true;
        }
        base.OnPreviewKeyDown(args);
    }

    private bool CanStep(int increment)
    {
        if (!TryPositive(Text, out var value))
            return increment > 0;
        var next = value + increment;
        return double.IsFinite(next) && next > 0 && next != value;
    }

    private void Step(int increment)
    {
        if (!CanStep(increment))
            return;
        var next = TryPositive(Text, out var value) ? value + increment : 1;
        SetCurrentValue(TextProperty, next.ToString("G", CultureInfo.CurrentCulture));
        GetBindingExpression(TextProperty)?.UpdateSource();
        Focus();
        SelectAll();
    }
}