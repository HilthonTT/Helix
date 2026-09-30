using Microsoft.Extensions.Logging;
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Theming;

public static class ThemeSwitcher
{
    private const string PreferenceKey = "Theme";

    public static event EventHandler? Changed;

    public static ThemeChoice Current { get; private set; } = ThemeChoice.System;

    public static ThemePalette Palette { get; private set; } = ThemePalettes.Light;

    public static void Initialize(AppBase app)
    {
        Current = ReadPreference();

        app.RequestedThemeChanged += (_, _) =>
        {
            if (Current == ThemeChoice.System)
            {
                ApplyPalette(app);
            }
        };

        ApplyPalette(app);
    }

    public static void Switch(ThemeChoice choice)
    {
        if (AppBase.Current is not AppBase app || choice == Current)
        {
            return;
        }

        Current = choice;

        try
        {
            Preferences.Default.Set(PreferenceKey, choice.ToString());
        }
        catch (Exception ex)
        {
            AppLog.For(typeof(ThemeSwitcher)).LogWarning(ex, "The theme could not be saved; it applies until Helix is closed.");
        }

        ApplyPalette(app);
    }

    private static ThemeChoice ReadPreference()
    {
        try
        {
            return Enum.TryParse(Preferences.Default.Get(PreferenceKey, nameof(ThemeChoice.System)), out ThemeChoice choice)
                && Enum.IsDefined(choice)
                ? choice
                : ThemeChoice.System;
        }
        catch (Exception ex)
        {
            AppLog.For(typeof(ThemeSwitcher)).LogWarning(ex, "The saved theme could not be read; following the system.");

            return ThemeChoice.System;
        }
    }

    private static void ApplyPalette(AppBase app)
    {
        AppTheme requested = Current == ThemeChoice.System
            ? AppTheme.Unspecified
            : ThemePalettes.For(Current, systemIsDark: false).IsDark ? AppTheme.Dark : AppTheme.Light;

        if (app.UserAppTheme != requested)
        {
            app.UserAppTheme = requested;
        }

        ThemePalette palette = ThemePalettes.For(Current, app.PlatformAppTheme == AppTheme.Dark);

        Palette = palette;

        ResourceDictionary resources = app.Resources;

        resources["Canvas"] = palette.Canvas;
        resources["Surface"] = palette.Surface;
        resources["SurfaceAlt"] = palette.SurfaceAlt;
        resources["SurfaceHover"] = palette.SurfaceHover;
        resources["SurfaceRaised"] = palette.SurfaceRaised;
        resources["Border"] = palette.Border;
        resources["BorderControl"] = palette.BorderControl;
        resources["BorderStrong"] = palette.BorderStrong;
        resources["Text"] = palette.Text;
        resources["TextMuted"] = palette.TextMuted;
        resources["TextFaint"] = palette.TextFaint;
        resources["Success"] = palette.Success;
        resources["Danger"] = palette.Danger;
        resources["Warning"] = palette.Warning;
        resources["Accent"] = palette.Accent;
        resources["AccentHover"] = palette.AccentHover;
        resources["AccentPressed"] = palette.AccentPressed;
        resources["AccentSoft"] = palette.AccentSoft;

        resources["CanvasBrush"] = new SolidColorBrush(palette.Canvas);
        resources["SurfaceBrush"] = new SolidColorBrush(palette.Surface);
        resources["SurfaceAltBrush"] = new SolidColorBrush(palette.SurfaceAlt);
        resources["SurfaceRaisedBrush"] = new SolidColorBrush(palette.SurfaceRaised);
        resources["BorderBrush"] = new SolidColorBrush(palette.Border);
        resources["AccentBrush"] = new SolidColorBrush(palette.Accent);
        resources["AccentWashBrush"] = new SolidColorBrush(palette.Accent.WithAlpha(palette.AccentWashAlpha));
        resources["SuccessWashBrush"] = new SolidColorBrush(palette.Success.WithAlpha(palette.StatusWashAlpha));
        resources["DangerWashBrush"] = new SolidColorBrush(palette.Danger.WithAlpha(palette.StatusWashAlpha));
        resources["WarningWashBrush"] = new SolidColorBrush(palette.Warning.WithAlpha(palette.StatusWashAlpha));

        resources["LogoImage"] = ImageSource.FromFile(palette.LogoImage);

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
