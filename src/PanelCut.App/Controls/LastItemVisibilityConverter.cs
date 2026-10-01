using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PanelCut.App.Controls;

// Values: row item, owning DataGrid, DataGrid.Items.Count (only to re-evaluate when rows are added or removed).
public sealed class LastItemVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [var item, DataGrid { Items.Count: > 0 } grid, ..] && ReferenceEquals(grid.Items[grid.Items.Count - 1], item)
            ? Visibility.Visible
            : Visibility.Hidden;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
