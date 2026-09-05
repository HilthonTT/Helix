using Microsoft.Maui.Controls.Shapes;

// Unaliased, `Application` binds to the Helix.Application namespace from here.
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Controls;

/// <summary>
/// A bounded whole number with its unit beside it and a pair of steppers: what the
/// settings page asks for instead of a bare text box.
/// </summary>
/// <remarks>
/// Four preferences were plain <c>Entry</c>s with a placeholder. Three problems came with
/// that, and this control is the answer to all three.
///
/// The <b>unit</b> lived in the row's title — "Timer count in seconds" — so the box itself
/// was a number with no dimension, and the retention and idle-lock rows did not say what
/// they counted at all. <see cref="Unit"/> puts it next to the figure.
///
/// <b>Zero means something</b> in three of the four: keep every audit log, never warn about
/// space, never lock. Nothing on screen said so. <see cref="OffText"/> replaces the unit
/// with that meaning the moment the value reaches zero, so the special case announces
/// itself rather than being documented somewhere the user is not looking.
///
/// And the <b>bounds were only enforced after the write</b>: the handler rejected the value
/// and the row rolled back, which is a failure banner for something the control could
/// simply not have allowed. <see cref="Minimum"/> and <see cref="Maximum"/> clamp instead.
/// The handler still validates — this is a keyboard, not a trust boundary.
///
/// The typed value is committed on Enter or on leaving the field, never per keystroke.
/// <c>SettingsDisplay</c> debounces these by 500ms precisely because "9" on the way to "90"
/// used to be saved and acted on; committing whole values means there is no such
/// intermediate to debounce, and the timer stays as a backstop rather than as the thing
/// standing between the user and a wrong setting.
/// </remarks>
internal sealed class NumberField : ContentView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(int),
        typeof(NumberField),
        0,
        BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(int), typeof(NumberField), 0);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(int), typeof(NumberField), int.MaxValue);

    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step), typeof(int), typeof(NumberField), 1);

    /// <summary>What the number counts — "seconds", "days", "%".</summary>
    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit),
        typeof(string),
        typeof(NumberField),
        string.Empty,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

    /// <summary>
    /// What zero means, where it means something — "Never", "Off", "Keep everything".
    /// Empty when zero is just a number.
    /// </summary>
    public static readonly BindableProperty OffTextProperty = BindableProperty.Create(
        nameof(OffText),
        typeof(string),
        typeof(NumberField),
        string.Empty,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

    private readonly Entry _entry;
    private readonly Label _suffix;
    private readonly Label _decrement;
    private readonly Label _increment;

    /// <summary>
    /// Set while <see cref="Render"/> writes the entry's text, so the write-back handlers
    /// do not treat the control's own update as something the user typed.
    /// </summary>
    private bool _rendering;

    public NumberField()
    {
        _decrement = StepLabel("−");
        _increment = StepLabel("+");

        _entry = new Entry
        {
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.Transparent,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 62,
            VerticalOptions = LayoutOptions.Center
        };

        _entry.Completed += (_, _) => Commit();
        _entry.Unfocused += (_, _) => Commit();

        _suffix = new Label
        {
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        _suffix.SetAppThemeColor(Label.TextColorProperty, Resource("TextMutedLight"), Resource("TextMutedDark"));

        var layout = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 2,
            Padding = new Thickness(4, 0)
        };

        layout.Add(_decrement);
        layout.Add(_entry, 1);
        layout.Add(_suffix, 2);
        layout.Add(_increment, 3);

        var border = new Border { Content = layout };

        if (AppBase.Current?.Resources.TryGetValue("Field", out object? field) == true && field is Style style)
        {
            border.Style = style;
        }
        else
        {
            border.StrokeShape = new RoundRectangle { CornerRadius = 10 };
        }

        Content = border;

        Render();
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string OffText
    {
        get => (string)GetValue(OffTextProperty);
        set => SetValue(OffTextProperty, value);
    }

    private Label StepLabel(string glyph)
    {
        var label = new Label
        {
            Text = glyph,
            FontSize = 16,
            WidthRequest = 26,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };

        label.SetAppThemeColor(Label.TextColorProperty, Resource("TextMutedLight"), Resource("TextMutedDark"));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Nudge(glyph == "+" ? Step : -Step);

        label.GestureRecognizers.Add(tap);

        // Fully qualified: unqualified, `Behaviors` binds to VisualElement's own
        // collection rather than to the namespace.
        Helix.App.Behaviors.Hover.SetCursor(label, true);

        return label;
    }

    private void Nudge(int by) => Value = Clamp(Value + by);

    /// <summary>
    /// Takes what is in the box, if it is a number, and clamps it into range. Anything
    /// else — an empty box, a pasted word — puts the last good value back rather than
    /// resolving to zero, which in three of these four fields would silently turn the
    /// setting off.
    /// </summary>
    private void Commit()
    {
        if (_rendering)
        {
            return;
        }

        if (int.TryParse(_entry.Text, out int typed))
        {
            Value = Clamp(typed);
        }

        // Unconditional: a clamped or rejected entry has to be redrawn even when Value
        // itself did not change, or the box goes on showing what the user typed.
        Render();
    }

    private int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    private void Render()
    {
        // Called from the constructor by way of the property defaults, before the fields
        // it touches are all assigned.
        if (_entry is null || _suffix is null)
        {
            return;
        }

        _rendering = true;

        try
        {
            _entry.Text = Value.ToString();

            _suffix.Text = Value == 0 && !string.IsNullOrEmpty(OffText) ? OffText : Unit;

            _decrement.Opacity = Value > Minimum ? 1 : 0.35;
            _increment.Opacity = Value < Maximum ? 1 : 0.35;
        }
        finally
        {
            _rendering = false;
        }
    }

    private static Color Resource(string key)
    {
        if (AppBase.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color)
        {
            return color;
        }

        return Colors.Grey;
    }
}
