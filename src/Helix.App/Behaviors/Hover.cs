using System.Reflection;

namespace Helix.App.Behaviors;

/// <summary>
/// Attached pointer-hover feedback.
/// </summary>
/// <remarks>
/// These are attached properties rather than a <see cref="Behavior"/> so they can be
/// applied from a <see cref="Style"/> — a view's <c>Behaviors</c> collection is
/// read-only and cannot be set by a style setter. That lets every card, list row and
/// icon button in the app share one hover treatment instead of hand-wiring each one.
///
/// The resting background is captured on the first pointer entry rather than at
/// attach time, because styles and control templates finish assigning backgrounds
/// after the attached property has already been set.
/// </remarks>
public static class Hover
{
    private const string RecognizerId = "Helix.Hover";
    private const string BackgroundAnimation = "Helix.Hover.Background";
    private const uint DurationMs = 140;

    /// <summary>Background colour to fade to while the pointer is over the view.</summary>
    public static readonly BindableProperty BackgroundProperty =
        BindableProperty.CreateAttached(
            "Background",
            typeof(Color),
            typeof(Hover),
            null,
            propertyChanged: OnFeedbackChanged);

    /// <summary>Scale to grow (or shrink) to while the pointer is over the view.</summary>
    public static readonly BindableProperty ScaleProperty =
        BindableProperty.CreateAttached(
            "Scale",
            typeof(double),
            typeof(Hover),
            1d,
            propertyChanged: OnFeedbackChanged);

    /// <summary>Opacity to fade to while the pointer is over the view.</summary>
    public static readonly BindableProperty OpacityProperty =
        BindableProperty.CreateAttached(
            "Opacity",
            typeof(double),
            typeof(Hover),
            1d,
            propertyChanged: OnFeedbackChanged);

    /// <summary>Shows the hand cursor over the view, marking it as clickable.</summary>
    public static readonly BindableProperty CursorProperty =
        BindableProperty.CreateAttached(
            "Cursor",
            typeof(bool),
            typeof(Hover),
            false,
            propertyChanged: OnCursorChanged);

    private static readonly BindableProperty RestingBackgroundProperty =
        BindableProperty.CreateAttached(
            "RestingBackground",
            typeof(Color),
            typeof(Hover),
            null);

    private static readonly BindableProperty RestingOpacityProperty =
        BindableProperty.CreateAttached(
            "RestingOpacity",
            typeof(double?),
            typeof(Hover),
            null);

    public static Color? GetBackground(BindableObject view) => (Color?)view.GetValue(BackgroundProperty);

    public static void SetBackground(BindableObject view, Color? value) => view.SetValue(BackgroundProperty, value);

    public static double GetScale(BindableObject view) => (double)view.GetValue(ScaleProperty);

    public static void SetScale(BindableObject view, double value) => view.SetValue(ScaleProperty, value);

    public static double GetOpacity(BindableObject view) => (double)view.GetValue(OpacityProperty);

    public static void SetOpacity(BindableObject view, double value) => view.SetValue(OpacityProperty, value);

    public static bool GetCursor(BindableObject view) => (bool)view.GetValue(CursorProperty);

    public static void SetCursor(BindableObject view, bool value) => view.SetValue(CursorProperty, value);

    private static void OnFeedbackChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is View view)
        {
            EnsureRecognizer(view);
        }
    }

    private static void EnsureRecognizer(View view)
    {
        bool alreadyAttached = view.GestureRecognizers
            .OfType<PointerGestureRecognizer>()
            .Any(recognizer => recognizer.ClassId == RecognizerId);

        if (alreadyAttached)
        {
            return;
        }

        var pointer = new PointerGestureRecognizer { ClassId = RecognizerId };

        pointer.PointerEntered += OnPointerEntered;
        pointer.PointerExited += OnPointerExited;

        view.GestureRecognizers.Add(pointer);
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (ResolveView(sender) is not View view || !view.IsEnabled)
        {
            return;
        }

        if (GetBackground(view) is Color hovered)
        {
            if (view.GetValue(RestingBackgroundProperty) is not Color resting)
            {
                resting = CurrentBackground(view);
                view.SetValue(RestingBackgroundProperty, resting);
            }

            AnimateBackground(view, resting, hovered);
        }

        double scale = GetScale(view);
        if (!IsOne(scale))
        {
            _ = view.ScaleToAsync(scale, DurationMs, Easing.CubicOut);
        }

        double opacity = GetOpacity(view);
        if (!IsOne(opacity))
        {
            view.SetValue(RestingOpacityProperty, view.Opacity);
            _ = view.FadeToAsync(opacity, DurationMs, Easing.CubicOut);
        }
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (ResolveView(sender) is not View view)
        {
            return;
        }

        if (view.GetValue(RestingBackgroundProperty) is Color resting)
        {
            AnimateBackground(view, CurrentBackground(view), resting);
        }

        if (!IsOne(GetScale(view)))
        {
            _ = view.ScaleToAsync(1d, DurationMs, Easing.CubicOut);
        }

        if (!IsOne(GetOpacity(view)))
        {
            double resume = view.GetValue(RestingOpacityProperty) as double? ?? 1d;
            _ = view.FadeToAsync(resume, DurationMs, Easing.CubicOut);
        }
    }

    private static View? ResolveView(object? sender)
    {
        return sender is GestureRecognizer recognizer ? recognizer.Parent as View : null;
    }

    private static Color CurrentBackground(View view)
    {
        return view.Background is SolidColorBrush brush
            ? brush.Color
            : view.BackgroundColor ?? Colors.Transparent;
    }

    private static void AnimateBackground(View view, Color from, Color to)
    {
        view.AbortAnimation(BackgroundAnimation);

        // Lerping through premultiplied-ish RGBA keeps fades from washing out when
        // either end is a translucent wash colour (the selected-row tints).
        new Animation(
                progress => view.Background = new SolidColorBrush(Lerp(from, to, progress)),
                0d,
                1d)
            .Commit(view, BackgroundAnimation, 16, DurationMs, Easing.CubicOut);
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        return Color.FromRgba(
            from.Red + ((to.Red - from.Red) * t),
            from.Green + ((to.Green - from.Green) * t),
            from.Blue + ((to.Blue - from.Blue) * t),
            from.Alpha + ((to.Alpha - from.Alpha) * t));
    }

    private static bool IsOne(double value) => Math.Abs(value - 1d) < 0.001;

    private static void OnCursorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view || newValue is not true)
        {
            return;
        }

        ApplyHandCursor(view);

        // The handler is usually not built yet when the style is applied, and it is
        // rebuilt whenever the view is recycled into a new template instance.
        view.HandlerChanged -= OnHandlerChanged;
        view.HandlerChanged += OnHandlerChanged;
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view && GetCursor(view))
        {
            ApplyHandCursor(view);
        }
    }

#if WINDOWS
    private static readonly PropertyInfo? ProtectedCursor = typeof(Microsoft.UI.Xaml.UIElement)
        .GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void ApplyHandCursor(View view)
    {
        if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        try
        {
            // WinUI only exposes ProtectedCursor to derived types, and MAUI never
            // surfaces it — reflection is the supported-in-practice workaround.
            ProtectedCursor?.SetValue(
                element,
                Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Helix: could not set hand cursor: {ex.Message}");
        }
    }
#else
    private static void ApplyHandCursor(View view)
    {
    }
#endif
}
