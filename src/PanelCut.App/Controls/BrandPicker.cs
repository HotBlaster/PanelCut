using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PanelCut.App.Controls;

public sealed record BrandChoice(string Name, bool IsNew)
{
    public string Label => IsNew ? $"Create \"{Name}\"" : Name;
}

public sealed class BrandPicker : TextBox
{
    public static readonly DependencyProperty BrandsProperty = DependencyProperty.Register(
        nameof(Brands), typeof(IEnumerable), typeof(BrandPicker), new PropertyMetadata(null));

    private readonly ListBox list;
    private readonly Popup popup;
    private bool picking;

    public BrandPicker()
    {
        list = new ListBox
        {
            ItemsSource = Choices, DisplayMemberPath = nameof(BrandChoice.Label), Focusable = false,
            MaxHeight = 220, MinWidth = 180, BorderThickness = new Thickness(0)
        };
        list.ItemContainerStyle = new Style(typeof(ListBoxItem))
        {
            Setters = { new Setter(FocusableProperty, false), new Setter(PaddingProperty, new Thickness(8, 5, 8, 5)) }
        };
        list.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (ItemsControl.ContainerFromElement(list, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: BrandChoice choice })
                Pick(choice);
        };
        popup = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Child = list, Background = Brushes.White, BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xD5, 0xD3))
            }
        };
        VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip = "Choose a brand, or type a new name to create it";
        Loaded += (_, _) =>
        {
            Focus();
            SelectAll();
            Rebuild();
            popup.IsOpen = IsKeyboardFocusWithin;
        };
        LostKeyboardFocus += (_, _) => popup.IsOpen = false;
    }

    public IEnumerable? Brands
    {
        get => (IEnumerable?)GetValue(BrandsProperty);
        set => SetValue(BrandsProperty, value);
    }

    public ObservableCollection<BrandChoice> Choices { get; } = [];
    public bool IsDropDownOpen => popup.IsOpen;

    protected override void OnTextChanged(TextChangedEventArgs args)
    {
        base.OnTextChanged(args);
        if (picking || !IsLoaded)
            return;
        Rebuild();
        popup.IsOpen = IsKeyboardFocusWithin;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key is Key.Down or Key.Up && !popup.IsOpen || key == Key.F4)
        {
            Rebuild();
            popup.IsOpen = !popup.IsOpen || key != Key.F4;
            args.Handled = true;
        }
        else if (key is Key.Down or Key.Up && Choices.Count > 0)
        {
            var next = list.SelectedIndex + (key == Key.Down ? 1 : -1);
            list.SelectedIndex = Math.Clamp(next, 0, Choices.Count - 1);
            list.ScrollIntoView(list.SelectedItem);
            args.Handled = true;
        }
        else if (key is Key.Enter or Key.Tab && popup.IsOpen && list.SelectedItem is BrandChoice choice)
        {
            Pick(choice);
            args.Handled = key == Key.Enter;
        }
        else if (key == Key.Escape && popup.IsOpen)
        {
            popup.IsOpen = false;
            args.Handled = true;
        }
        base.OnPreviewKeyDown(args);
    }

    private void Rebuild()
    {
        var typed = Text.Trim();
        var names = Brands?.OfType<string>() ?? [];
        var matches = names.Where(name => name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToArray();
        Choices.Clear();
        foreach (var name in matches)
            Choices.Add(new BrandChoice(name, false));
        if (typed.Length > 0 && !names.Any(name => string.Equals(name, typed, StringComparison.OrdinalIgnoreCase)))
            Choices.Add(new BrandChoice(typed, true));
        list.SelectedIndex = typed.Length > 0 && Choices.Count > 0 ? 0 : -1;
    }

    private void Pick(BrandChoice choice)
    {
        picking = true;
        try
        {
            SetCurrentValue(TextProperty, choice.Name);
            GetBindingExpression(TextProperty)?.UpdateSource();
            CaretIndex = Text.Length;
        }
        finally { picking = false; }
        popup.IsOpen = false;
    }
}
