using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Tasker_App.Controls;

/// <summary>
/// A lightweight wrapping panel (WinUI has no built-in one): lays children out left-to-right,
/// moving to the next line when they no longer fit. Used for the Assistant's suggestion chips.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }
    public double VerticalSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var max = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double lineWidth = 0, lineHeight = 0, totalWidth = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = child.DesiredSize;

            if (lineWidth > 0 && lineWidth + HorizontalSpacing + d.Width > max)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = d.Width;
                lineHeight = d.Height;
            }
            else
            {
                lineWidth += (lineWidth > 0 ? HorizontalSpacing : 0) + d.Width;
                lineHeight = Math.Max(lineHeight, d.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;
        return new Size(double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            var d = child.DesiredSize;
            if (x > 0 && x + d.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, d.Height);
        }

        return finalSize;
    }
}
