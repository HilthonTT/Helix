# Themes and icons

Design rationale moved out of CLAUDE.md. Read before changing this area; update it when the reasoning changes.

## Themes

Settings → Preferences offers **Crimson**, **System**, **Light**, **Dark** and four custom
palettes (Midnight, Amethyst, Sand, Glacier). **Crimson is the default** — a near-black canvas
with the brand's crimson accent, the same look as the documentation site — and is what an
install with no saved choice, or an unreadable one, gets. A user who had picked **System**
keeps it. Light and Dark carry the crimson accent too, so the logo never sits beside an
accent from the old orange brand. Every color that differs between themes is a
**`{DynamicResource}` token**, never an `AppThemeBinding` and never a `StaticResource`:
`AppThemeBinding` only knows two themes, and a `StaticResource` is resolved once, so a theme
picked at runtime would have repainted nothing. The tokens are written into
`Application.Resources` by `Theming/ThemeSwitcher` from a `ThemePalette` record, and every
bound property follows. **A new color goes into `ThemePalette` and every palette, and is
referenced as `{DynamicResource Name}`.** C# that builds views does the same with
`SetDynamicResource` (`NotificationHost`, `NumberField`); code that needs a value rather
than a binding — the dashboard donut — reads `ThemeSwitcher.Palette`.

The tokens: `Canvas`, `Surface`, `SurfaceAlt`, `SurfaceHover`, `SurfaceRaised` (a field or
chip: `Surface` on a light theme, `SurfaceAlt` on a dark one), `Border`, `BorderControl`
(an input's outline, likewise), `BorderStrong`, `Text`, `TextMuted`, `TextFaint`, `Success`,
`Danger`, `Warning`, `Accent`, `AccentHover`, `AccentPressed`, `AccentSoft`, plus the
brushes `CanvasBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `SurfaceRaisedBrush`,
`BorderBrush`, `AccentBrush` and the translucent `AccentWashBrush`, `SuccessWashBrush`,
`DangerWashBrush`, `WarningWashBrush`, which are derived from the palette rather than
listed in it. `Colors.xaml` keeps only what does not change with the theme: the brand
colors, the brand gradient behind the sign-in artwork, `DangerSolid` for the destructive
button, `OnAccent`, and the scrim.

The choice is stored in MAUI `Preferences`, **not** in the `Settings` table, because it has
to apply to the sign-in and lock pages, which are drawn before any account's settings can
be read. It is per Windows user rather than per Helix account for the same reason.
`ThemeSwitcher` also sets `UserAppTheme` to the palette's light or dark base, so WinUI's own
chrome — the title bar, text selection, picker drop-downs — matches, and under **System**
it re-applies on `RequestedThemeChanged`.

The logo is a double helix in a crimson gradient, square, drawn on a 32-unit grid; the
website's `components/logo.tsx` and `public/logo.svg` carry the same paths, so **change it in
both places**. The `LogoImage` resource is `helix_logo.png` (a deeper gradient, for contrast
on white) on light palettes and `helix_logo_on_dark.png` on dark ones; the `Image`s bind it
dynamically. The sign-in and lock pages' artwork is `brand_background.svg` — the helix on a
near-black field with a crimson bloom — behind `BrandGradientBrush`, and is dark on purpose:
the "Helix" caption over its lower left is white whatever the theme. **No SVG under
`Resources/` may use a `<filter>`** (`feGaussianBlur` and friends): on the Mac head the
resizetizer's Svg.Skia calls an `SKImageFilter` overload its SkiaSharp does not have and the
build fails with `MAUIR0001 … MissingMethodException`, while Windows builds the same file
happily. Soft glows are radial gradients instead.

## The tray and taskbar icon

The Windows app icon (`MauiIcon`) has **no background color**: `Color="#050505"` filled
the whole square, and that black tile is what the taskbar and the tray showed. The Mac head
keeps the color, since a Mac app icon is expected to be a full tile. A stale icon survives
an incremental build; delete the app's `obj/.../resizetizer` folder if the tile persists.

`Helix.Infrastructure/Desktop/Icons` carries two `.ico` files —
`helix-light-taskbar` (the deeper crimson) and `helix-dark-taskbar` (the brighter one), 16 to
256 px, rendered from the logo SVG at each size rather than scaled from one — and
`ShellIcons.CurrentPath()` picks by `SystemUsesLightTheme`, which is the **taskbar's**
setting, independent of both Windows' app mode and the Helix theme. `WindowsTrayIcon` loads
it at the small-icon size and swaps it on `WM_SETTINGCHANGE` "ImmersiveColorSet";
`MauiProgram` sets the same file as the window icon, which is what the running app's taskbar
button shows, at creation and again on activation. Library content is copied by the Windows
App SDK into a folder named after the assembly (`Helix.Infrastructure\Icons`), which is why
`CurrentPath` looks there before the root. When neither file is found the tray falls back to
the executable's icon.

