namespace Helix.App.Layouts;

using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using StackLayoutManager = Microsoft.Maui.Layouts.StackLayoutManager;

internal sealed class HorizontalWrapLayoutManager : StackLayoutManager
{
    private readonly HorizontalWrapLayout _horizontalStackLayout;

    public HorizontalWrapLayoutManager(HorizontalWrapLayout stackLayout)
        : base(stackLayout)
    {
        _horizontalStackLayout = stackLayout;
    }

    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        Thickness padding = Stack.Padding;
        widthConstraint -= padding.HorizontalThickness;

        (double totalWidth, double totalHeight) = CalculateDimensions(widthConstraint, heightConstraint);

        return ApplyPaddingAndConstraints(totalWidth, totalHeight, padding, widthConstraint, heightConstraint);
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        Thickness padding = Stack.Padding;

        double top = padding.Top + bounds.Top;
        double left = padding.Left + bounds.Left;

        return ArrangeViews(bounds, top, left, padding);
    }

    private (double totalWidth, double totalHeight) CalculateDimensions(double widthConstraint, double heightConstraint)
    {
        double currentRowWidth = 0;
        double currentRowHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;

        for (int i = 0; i < _horizontalStackLayout.Count; i++)
        {
            IView child = _horizontalStackLayout[i];
            if (child.Visibility is Visibility.Collapsed)
            {
                continue;
            }

            Size measure = child.Measure(double.PositiveInfinity, heightConstraint);

            if (IsNewRowNeeded(currentRowWidth, measure.Width, widthConstraint))
            {
                totalWidth = UpdateTotalWidth(totalWidth, currentRowWidth);
                totalHeight += currentRowHeight + _horizontalStackLayout.Spacing;
                ResetRowDimensions(out currentRowWidth, out currentRowHeight, measure);
            }
            else
            {
                UpdateCurrentRowDimensions(ref currentRowWidth, ref currentRowHeight, measure, i);
            }
        }

        // Account for the last row
        totalWidth = Math.Max(totalWidth, currentRowWidth);
        totalHeight += currentRowHeight;

        return (totalWidth, totalHeight);
    }

    private static bool IsNewRowNeeded(double currentRowWidth, double childWidth, double widthConstraint) =>
        currentRowWidth + childWidth > widthConstraint;

    private static double UpdateTotalWidth(double totalWidth, double currentRowWidth) =>
        Math.Max(totalWidth, currentRowWidth);

    private static void ResetRowDimensions(out double currentRowWidth, out double currentRowHeight, Size measure)
    {
        currentRowWidth = measure.Width;
        currentRowHeight = measure.Height;
    }

    private void UpdateCurrentRowDimensions(ref double currentRowWidth, ref double currentRowHeight, Size measure, int index)
    {
        currentRowWidth += measure.Width;
        currentRowHeight = Math.Max(currentRowHeight, measure.Height);

        if (index < _horizontalStackLayout.Count - 1)
        {
            currentRowWidth += _horizontalStackLayout.Spacing;
        }
    }

    private Size ApplyPaddingAndConstraints(double totalWidth, double totalHeight, Thickness padding, double widthConstraint, double heightConstraint)
    {
        totalWidth += padding.HorizontalThickness;
        totalHeight += padding.VerticalThickness;

        double finalHeight = ResolveConstraints(heightConstraint, Stack.Height, totalHeight, Stack.MinimumHeight, Stack.MaximumHeight);
        double finalWidth = ResolveConstraints(widthConstraint, Stack.Width, totalWidth, Stack.MinimumWidth, Stack.MaximumWidth);

        return new Size(finalWidth, finalHeight);
    }

    private Size ArrangeViews(Rect bounds, double top, double left, Thickness padding)
    {
        double currentRowTop = top;
        double currentX = left;
        double currentRowHeight = 0;
        double maxStackWidth = currentX;

        for (int i = 0; i < _horizontalStackLayout.Count; i++)
        {
            IView child = _horizontalStackLayout[i];
            if (child.Visibility is Visibility.Collapsed)
            {
                continue;
            }

            if (IsNewRowNeeded(currentX, child.DesiredSize.Width, bounds.Right))
            {
                maxStackWidth = Math.Max(maxStackWidth, currentX);
                MoveToNextRow(ref currentX, ref currentRowTop, currentRowHeight, padding);
                currentRowHeight = 0;
            }

            Rect destination = ArrangeChild(child, ref currentX, currentRowTop);
            currentRowHeight = Math.Max(currentRowHeight, destination.Height);
        }

        return CalculateActualSize(maxStackWidth, currentRowTop, currentRowHeight, bounds);
    }

    private void MoveToNextRow(ref double currentX, ref double currentRowTop, double currentRowHeight, Thickness padding)
    {
        currentX = padding.Left;
        currentRowTop += currentRowHeight + _horizontalStackLayout.Spacing;
    }

    private Rect ArrangeChild(IView child, ref double currentX, double currentRowTop)
    {
        var destination = new Rect(currentX, currentRowTop, child.DesiredSize.Width, child.DesiredSize.Height);

        child.Arrange(destination);
        currentX += destination.Width + _horizontalStackLayout.Spacing;

        return destination;
    }

    private Size CalculateActualSize(double maxStackWidth, double currentRowTop, double currentRowHeight, Rect bounds)
    {
        var actual = new Size(maxStackWidth, currentRowTop + currentRowHeight);

        return actual.AdjustForFill(bounds, Stack);
    }
}
