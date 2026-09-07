using Microsoft.Maui.Controls.Shapes;

using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Controls;

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
        nameof(Minimum),
        typeof(int),
        typeof(NumberField),
        0,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum),
        typeof(int),
        typeof(NumberField),
        int.MaxValue,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step), typeof(int), typeof(NumberField), 1);

    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit),
        typeof(string),
        typeof(NumberField),
        string.Empty,
        propertyChanged: (bindable, _, _) => ((NumberField)bindable).Render());

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

        Helix.App.Behaviors.Hover.SetCursor(label, true);

        return label;
    }

    private void Nudge(int by) => Value = Clamp(Value + by);

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

        Render();
    }

    private int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    private void Render()
    {
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
