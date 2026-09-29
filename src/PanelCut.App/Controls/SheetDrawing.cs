using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PanelCut.Core.Models;
using PanelCut.Core.Optimization;

namespace PanelCut.App.Controls;

public sealed class SheetDrawing : FrameworkElement
{
    private readonly SheetLayout sheet;
    private readonly LengthUnit unit;
    private readonly List<(Rect Bounds, string Text)> hitAreas = [];
    private readonly string[] palette = ["#BBDDD1", "#F3D893", "#B9D4EB", "#EDBAB5", "#CECF9A", "#CBBFE2"];

    public SheetDrawing(SheetLayout sheet, LengthUnit unit)
    {
        this.sheet = sheet;
        this.unit = unit;
        ClipToBounds = true;
        ToolTip = null;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        hitAreas.Clear();
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth < 80 || ActualHeight < 80)
            return;
        var stock = sheet.Stock;
        var scale = Math.Min((ActualWidth - 40) / stock.Width, (ActualHeight - 52) / stock.Height);
        if (!double.IsFinite(scale) || scale <= 0)
            return;
        var left = (ActualWidth - stock.Width * scale) / 2;
        var top = (ActualHeight - stock.Height * scale) / 2 + 8;
        var outer = new Rect(left, top, stock.Width * scale, stock.Height * scale);
        var outline = new Pen(Brush("#354945"), 1);
        context.DrawRectangle(Hatch("#E7D7C2", "#AD8960"), outline, outer);
        var usable = Map(new LayoutRectangle(stock.EdgeTrim, stock.EdgeTrim, stock.UsableWidth, stock.UsableHeight), scale, left, top);
        context.DrawRectangle(Brushes.White, outline, usable);
        Text(context, $"{stock.Label}   |   {Length(stock.Width)} x {Length(stock.Height)} {UnitName}   |   {stock.Material} / {stock.MaterialType} / {Length(stock.Thickness)} {UnitName}",
            new Rect(left, Math.Max(2, top - 30), outer.Width, 24), 13, false);
        foreach (var scrap in sheet.RemainingScraps)
        {
            var bounds = Map(scrap, scale, left, top);
            context.DrawRectangle(Hatch("#F0F3F2", "#C3CECA"), new Pen(Brush("#93A49E"), 0.8), bounds);
            hitAreas.Add((bounds, $"Remaining scrap: {Length(scrap.Width)} x {Length(scrap.Height)} {UnitName}"));
        }
        foreach (var placement in sheet.Placements)
        {
            var bounds = Map(placement.Bounds, scale, left, top);
            context.DrawRectangle(GroupBrush(placement.Part.GroupTag), outline, bounds);
            var label = string.IsNullOrWhiteSpace(placement.Part.Label) ? "Part" : placement.Part.Label;
            var dimensions = $"{Length(placement.Bounds.Width)} x {Length(placement.Bounds.Height)} {UnitName}";
            var details = $"{label} #{placement.CopyIndex}\n{dimensions}\n{placement.Part.Material} / {placement.Part.MaterialType} / {Length(placement.Part.Thickness)} {UnitName}";
            if (placement.Part.GroupTag.Length > 0)
                details += $"\n{placement.Part.GroupTag}";
            if (placement.IsRotated)
                details += "\nRotated 90 degrees";
            hitAreas.Add((bounds, details));
            if (bounds.Width >= 38 && bounds.Height >= 24)
            {
                var content = new Rect(bounds.X + 5, bounds.Y + 3, Math.Max(1, bounds.Width - 10), Math.Max(1, bounds.Height - 6));
                Text(context, bounds.Height >= 52 && bounds.Width >= 115 ? $"{label}\n{dimensions}" : label, content, bounds.Width >= 120 ? 13 : 11, true);
            }
        }
        context.DrawRectangle(null, outline, outer);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        var point = args.GetPosition(this);
        var match = hitAreas.LastOrDefault(area => area.Bounds.Contains(point));
        var text = match.Text ?? $"{sheet.Stock.Label}\n{sheet.Stock.Material} / {sheet.Stock.MaterialType} / {Length(sheet.Stock.Thickness)} {UnitName}\nEdge trim: {Length(sheet.Stock.EdgeTrim)} {UnitName}";
        if (!Equals(ToolTip, text))
            ToolTip = text;
    }

    private void Text(DrawingContext context, string text, Rect bounds, double fontSize, bool centered)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Bahnschrift"), fontSize, Brush("#223630"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = bounds.Width, MaxTextHeight = Math.Max(fontSize + 2, bounds.Height),
            Trimming = TextTrimming.CharacterEllipsis, TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
        };
        context.PushClip(new RectangleGeometry(bounds));
        context.DrawText(formatted, new Point(bounds.X, centered ? bounds.Y + Math.Max(0, (bounds.Height - formatted.Height) / 2) : bounds.Y));
        context.Pop();
    }

    private Brush GroupBrush(string group)
    {
        if (group.Length == 0)
            return Brush("#D5DDDB");
        uint hash = 2166136261;
        foreach (var character in group)
            hash = unchecked((hash ^ character) * 16777619);
        return Brush(palette[hash % (uint)palette.Length]);
    }

    private static Brush Hatch(string background, string line)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brush(background), null, new Rect(0, 0, 8, 8));
            context.DrawLine(new Pen(Brush(line), 0.7), new Point(0, 8), new Point(8, 0));
        }
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 8, 8)
        };
        brush.Freeze();
        return brush;
    }

    private static Brush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private string UnitName => unit == LengthUnit.Millimetres ? "mm" : "inch";
    private string Length(double value) => UnitConversion.FromMillimetres(value, unit).ToString("0.###", CultureInfo.CurrentCulture);
    private static Rect Map(LayoutRectangle rectangle, double scale, double left, double top) =>
        new(left + rectangle.X * scale, top + rectangle.Y * scale, rectangle.Width * scale, rectangle.Height * scale);
}