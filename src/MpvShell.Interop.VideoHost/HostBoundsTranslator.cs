namespace MpvShell.Interop.VideoHost;

public readonly record struct HostRect(int X, int Y, int Width, int Height);

public static class HostBoundsTranslator
{
    public static HostRect Translate(double x, double y, double width, double height, double rasterizationScale) =>
        new(
            (int)Math.Round(x * rasterizationScale),
            (int)Math.Round(y * rasterizationScale),
            (int)Math.Round(width * rasterizationScale),
            (int)Math.Round(height * rasterizationScale));
}
