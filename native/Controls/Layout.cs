using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LedControl.Controls;

/// <summary>
/// Equal-width columns that fit the space: as many columns of at least <see cref="MinItemWidth"/> as fit,
/// stretched to fill the row (like CSS grid "repeat(auto-fit, minmax(min, 1fr))"). Items in a row share its height.
/// </summary>
public sealed partial class AdaptiveGrid : Panel
{
    public double MinItemWidth { get; set; } = 200;
    public double ColumnSpacing { get; set; } = 12;
    public double RowSpacing { get; set; } = 12;
    public int MaxColumns { get; set; } = int.MaxValue;

    double[] rowHeights = [];
    int columns = 1;

    int VisibleCount => Children.Count(c => c.Visibility == Visibility.Visible);

    int ColumnsFor(double width, int count)
    {
        if (count == 0) return 1;
        if (double.IsInfinity(width)) return Math.Min(count, MaxColumns);
        int c = (int)Math.Floor((width + ColumnSpacing) / (MinItemWidth + ColumnSpacing));
        return Math.Clamp(c, 1, Math.Min(MaxColumns, Math.Max(1, count)));
    }

    protected override Size MeasureOverride(Size available)
    {
        var items = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        columns = ColumnsFor(available.Width, items.Count);
        double width = double.IsInfinity(available.Width) ? MinItemWidth * columns + ColumnSpacing * (columns - 1) : available.Width;
        double itemWidth = Math.Max(0, (width - ColumnSpacing * (columns - 1)) / columns);
        int rows = (items.Count + columns - 1) / columns;
        rowHeights = new double[rows];
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeights[i / columns] = Math.Max(rowHeights[i / columns], items[i].DesiredSize.Height);
        }
        double height = rowHeights.Sum() + RowSpacing * Math.Max(0, rows - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var items = Children.Where(c => c.Visibility == Visibility.Visible).ToList();
        int cols = ColumnsFor(final.Width, items.Count);
        if (cols != columns || rowHeights.Length != (items.Count + cols - 1) / cols)
        {
            // Measured for a different width: measure again for this one.
            MeasureOverride(new Size(final.Width, double.PositiveInfinity));
            cols = columns;
        }
        double itemWidth = Math.Max(0, (final.Width - ColumnSpacing * (cols - 1)) / cols);
        double y = 0;
        for (int i = 0; i < items.Count; i++)
        {
            int row = i / cols, col = i % cols;
            if (col == 0 && row > 0) y += rowHeights[row - 1] + RowSpacing;
            items[i].Arrange(new Rect(col * (itemWidth + ColumnSpacing), y, itemWidth, rowHeights[row]));
        }
        return final;
    }
}

/// <summary>Lays children out left to right, wrapping onto new lines.</summary>
public sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 8;
    public double VerticalSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available) => Layout(available.Width, arrange: false);

    protected override Size ArrangeOverride(Size final)
    {
        Layout(final.Width, arrange: true);
        return final;
    }

    Size Layout(double maxWidth, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            if (!arrange) child.Measure(new Size(maxWidth, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > maxWidth)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, s.Height);
            widest = Math.Max(widest, x - HorizontalSpacing);
        }
        return new Size(widest, y + lineHeight);
    }
}
