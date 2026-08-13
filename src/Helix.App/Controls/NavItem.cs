namespace Helix.App.Controls;

/// <summary>
/// A <see cref="RadioButton"/> shaped for navigation: an icon glyph, a label and an
/// optional description.
/// </summary>
/// <remarks>
/// A plain <c>RadioButton.Content</c> can only be recoloured from inside a control
/// template through named parts, so the pieces are exposed as bindable properties and
/// rendered by <c>NavItemTemplate</c> / <c>NavSectionTemplate</c>. That is what lets
/// the active item tint its icon and label, not just its background.
/// Selection state still flows through <c>RadioButtonGroup</c> as usual.
/// </remarks>
public sealed class NavItem : RadioButton
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(NavItem), string.Empty);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(NavItem), string.Empty);

    /// <summary>FontAwesome glyph shown ahead of the label.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Secondary line rendered under the label by <c>NavSectionTemplate</c>.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
