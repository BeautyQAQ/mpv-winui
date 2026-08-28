namespace MpvShell.Rendering.WinUI;

public readonly record struct VideoSurfaceSize
{
    public VideoSurfaceSize(double logicalWidth, double logicalHeight, double rasterizationScale)
    {
        if (!double.IsFinite(logicalWidth) || logicalWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }

        if (!double.IsFinite(logicalHeight) || logicalHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }

        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        }

        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        RasterizationScale = rasterizationScale;
    }

    public double LogicalWidth { get; }

    public double LogicalHeight { get; }

    public double RasterizationScale { get; }
}
