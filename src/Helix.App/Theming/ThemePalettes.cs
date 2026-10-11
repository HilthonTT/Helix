namespace Helix.App.Theming;

public static class ThemePalettes
{
    public static readonly ThemePalette Crimson = new(
        IsDark: true,
        Canvas: Color.FromArgb("#050505"),
        Surface: Color.FromArgb("#0D0D0D"),
        SurfaceAlt: Color.FromArgb("#151314"),
        SurfaceHover: Color.FromArgb("#1C1819"),
        Border: Color.FromArgb("#221D1E"),
        BorderStrong: Color.FromArgb("#3A3233"),
        Text: Color.FromArgb("#F7F7F7"),
        TextMuted: Color.FromArgb("#B3ADAE"),
        TextFaint: Color.FromArgb("#858585"),
        Success: Color.FromArgb("#2ECC70"),
        Danger: Color.FromArgb("#FF6B7D"),
        Warning: Color.FromArgb("#F9B81F"),
        Accent: Color.FromArgb("#F0193D"),
        AccentHover: Color.FromArgb("#FF4D67"),
        AccentPressed: Color.FromArgb("#C4142F"),
        AccentSoft: Color.FromArgb("#FF8095"));

    public static readonly ThemePalette Light = new(
        IsDark: false,
        Canvas: Color.FromArgb("#F7F6F4"),
        Surface: Color.FromArgb("#FFFFFF"),
        SurfaceAlt: Color.FromArgb("#F2F1EF"),
        SurfaceHover: Color.FromArgb("#EAE8E5"),
        Border: Color.FromArgb("#E4E1DD"),
        BorderStrong: Color.FromArgb("#CFC9C4"),
        Text: Color.FromArgb("#1C1917"),
        TextMuted: Color.FromArgb("#57534E"),
        TextFaint: Color.FromArgb("#8A827C"),
        Success: Color.FromArgb("#0E9F6E"),
        Danger: Color.FromArgb("#DC2626"),
        Warning: Color.FromArgb("#B45309"),
        Accent: Color.FromArgb("#D81B3C"),
        AccentHover: Color.FromArgb("#F0193D"),
        AccentPressed: Color.FromArgb("#B3122E"),
        AccentSoft: Color.FromArgb("#FF4D67"));

    public static readonly ThemePalette Dark = new(
        IsDark: true,
        Canvas: Color.FromArgb("#121110"),
        Surface: Color.FromArgb("#1B1918"),
        SurfaceAlt: Color.FromArgb("#242120"),
        SurfaceHover: Color.FromArgb("#2C2827"),
        Border: Color.FromArgb("#302C2A"),
        BorderStrong: Color.FromArgb("#4A4441"),
        Text: Color.FromArgb("#FAFAF9"),
        TextMuted: Color.FromArgb("#A8A29E"),
        TextFaint: Color.FromArgb("#7C736E"),
        Success: Color.FromArgb("#34D399"),
        Danger: Color.FromArgb("#F87171"),
        Warning: Color.FromArgb("#FBBF24"),
        Accent: Color.FromArgb("#F0193D"),
        AccentHover: Color.FromArgb("#FF4D67"),
        AccentPressed: Color.FromArgb("#C4142F"),
        AccentSoft: Color.FromArgb("#FF8095"));

    public static readonly ThemePalette Midnight = new(
        IsDark: true,
        Canvas: Color.FromArgb("#0B1120"),
        Surface: Color.FromArgb("#111827"),
        SurfaceAlt: Color.FromArgb("#1A2333"),
        SurfaceHover: Color.FromArgb("#223047"),
        Border: Color.FromArgb("#243044"),
        BorderStrong: Color.FromArgb("#3A4A63"),
        Text: Color.FromArgb("#F1F5F9"),
        TextMuted: Color.FromArgb("#94A3B8"),
        TextFaint: Color.FromArgb("#64748B"),
        Success: Color.FromArgb("#34D399"),
        Danger: Color.FromArgb("#F87171"),
        Warning: Color.FromArgb("#FBBF24"),
        Accent: Color.FromArgb("#3B82F6"),
        AccentHover: Color.FromArgb("#60A5FA"),
        AccentPressed: Color.FromArgb("#2563EB"),
        AccentSoft: Color.FromArgb("#38BDF8"));

    public static readonly ThemePalette Amethyst = new(
        IsDark: true,
        Canvas: Color.FromArgb("#120F1A"),
        Surface: Color.FromArgb("#1A1624"),
        SurfaceAlt: Color.FromArgb("#231E30"),
        SurfaceHover: Color.FromArgb("#2C263C"),
        Border: Color.FromArgb("#2F2940"),
        BorderStrong: Color.FromArgb("#4A4160"),
        Text: Color.FromArgb("#F5F3FF"),
        TextMuted: Color.FromArgb("#A9A3BD"),
        TextFaint: Color.FromArgb("#776F8C"),
        Success: Color.FromArgb("#34D399"),
        Danger: Color.FromArgb("#F87171"),
        Warning: Color.FromArgb("#FBBF24"),
        Accent: Color.FromArgb("#8B5CF6"),
        AccentHover: Color.FromArgb("#A78BFA"),
        AccentPressed: Color.FromArgb("#7C3AED"),
        AccentSoft: Color.FromArgb("#F0ABFC"));

    public static readonly ThemePalette Sand = new(
        IsDark: false,
        Canvas: Color.FromArgb("#F3EEE4"),
        Surface: Color.FromArgb("#FBF8F2"),
        SurfaceAlt: Color.FromArgb("#EFE8DB"),
        SurfaceHover: Color.FromArgb("#E6DDCC"),
        Border: Color.FromArgb("#E0D6C4"),
        BorderStrong: Color.FromArgb("#C9BBA3"),
        Text: Color.FromArgb("#2B2419"),
        TextMuted: Color.FromArgb("#5F5446"),
        TextFaint: Color.FromArgb("#8C7F6C"),
        Success: Color.FromArgb("#2F855A"),
        Danger: Color.FromArgb("#C53030"),
        Warning: Color.FromArgb("#B45309"),
        Accent: Color.FromArgb("#B4532A"),
        AccentHover: Color.FromArgb("#C8663A"),
        AccentPressed: Color.FromArgb("#963F1C"),
        AccentSoft: Color.FromArgb("#D69E2E"));

    public static readonly ThemePalette Glacier = new(
        IsDark: false,
        Canvas: Color.FromArgb("#F1F5F9"),
        Surface: Color.FromArgb("#FFFFFF"),
        SurfaceAlt: Color.FromArgb("#EEF2F7"),
        SurfaceHover: Color.FromArgb("#E2E8F0"),
        Border: Color.FromArgb("#DCE3EC"),
        BorderStrong: Color.FromArgb("#C3CDDA"),
        Text: Color.FromArgb("#0F172A"),
        TextMuted: Color.FromArgb("#475569"),
        TextFaint: Color.FromArgb("#7B8798"),
        Success: Color.FromArgb("#0E9F6E"),
        Danger: Color.FromArgb("#DC2626"),
        Warning: Color.FromArgb("#B45309"),
        Accent: Color.FromArgb("#0E7490"),
        AccentHover: Color.FromArgb("#0891B2"),
        AccentPressed: Color.FromArgb("#155E75"),
        AccentSoft: Color.FromArgb("#0EA5E9"));

    public static ThemePalette For(ThemeChoice choice, bool systemIsDark) => choice switch
    {
        ThemeChoice.Crimson => Crimson,
        ThemeChoice.Light => Light,
        ThemeChoice.Dark => Dark,
        ThemeChoice.Midnight => Midnight,
        ThemeChoice.Amethyst => Amethyst,
        ThemeChoice.Sand => Sand,
        ThemeChoice.Glacier => Glacier,
        _ => systemIsDark ? Dark : Light
    };
}
