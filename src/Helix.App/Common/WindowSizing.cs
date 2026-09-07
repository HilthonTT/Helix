namespace Helix.App.Common;

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height);

internal static class WindowSizing
{
    private const double TargetAspectRatio = 16.0 / 9.0;

    public static WindowBounds Calculate(int screenWidth, int screenHeight)
    {
        double scalingFactor =
            (screenWidth >= 3456 && screenWidth <= 4224) ||
            (screenHeight >= 1944 && screenHeight <= 2376)
                ? 0.9
                : 0.8;

        int windowWidth = (int)(screenWidth * scalingFactor);
        int windowHeight = (int)(windowWidth / TargetAspectRatio);

        if (windowHeight > screenHeight * scalingFactor)
        {
            windowHeight = (int)(screenHeight * scalingFactor);
            windowWidth = (int)(windowHeight * TargetAspectRatio);
        }

        int posX = (screenWidth - windowWidth) / 2;
        int posY = (screenHeight - windowHeight) / 2;

        return new WindowBounds(posX, posY, windowWidth, windowHeight);
    }
}
