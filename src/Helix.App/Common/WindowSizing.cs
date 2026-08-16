namespace Helix.App.Common;

/// <summary>Where the main window should open, in device-independent units.</summary>
internal readonly record struct WindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// The app's opening-window rule, shared by both desktop heads.
/// </summary>
/// <remarks>
/// Windows applies this through <c>AppWindow.MoveAndResize</c> during the lifecycle
/// event; Mac Catalyst has no AppWindow, so it applies the same numbers to the MAUI
/// <see cref="Microsoft.Maui.Controls.Window"/> geometry properties. Keeping the
/// arithmetic here means the two heads cannot drift apart.
/// </remarks>
internal static class WindowSizing
{
    private const double TargetAspectRatio = 16.0 / 9.0;

    public static WindowBounds Calculate(int screenWidth, int screenHeight)
    {
        // Determine scaling factor based on resolution
        double scalingFactor =
            (screenWidth >= 3456 && screenWidth <= 4224) ||
            (screenHeight >= 1944 && screenHeight <= 2376)
                ? 0.9
                : 0.8;

        // Calculate the window size to maintain a 16:9 aspect ratio
        int windowWidth = (int)(screenWidth * scalingFactor);
        int windowHeight = (int)(windowWidth / TargetAspectRatio);

        // Ensure the window height fits within the screen's height
        if (windowHeight > screenHeight * scalingFactor)
        {
            windowHeight = (int)(screenHeight * scalingFactor);
            windowWidth = (int)(windowHeight * TargetAspectRatio);
        }

        // Center the window on the screen
        int posX = (screenWidth - windowWidth) / 2;
        int posY = (screenHeight - windowHeight) / 2;

        return new WindowBounds(posX, posY, windowWidth, windowHeight);
    }
}
