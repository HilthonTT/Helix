namespace Helix.App.Theming;

public sealed record ThemePalette(
    bool IsDark,
    Color Canvas,
    Color Surface,
    Color SurfaceAlt,
    Color SurfaceHover,
    Color Border,
    Color BorderStrong,
    Color Text,
    Color TextMuted,
    Color TextFaint,
    Color Success,
    Color Danger,
    Color Warning,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentSoft)
{
    public Color SurfaceRaised => IsDark ? SurfaceAlt : Surface;

    public Color BorderControl => IsDark ? Border : BorderStrong;

    public float AccentWashAlpha => IsDark ? 0.14f : 0.086f;

    public float StatusWashAlpha => IsDark ? 0.15f : 0.12f;

    public string LogoImage => IsDark ? "logo_on_dark.png" : "logoipsum.png";
}
