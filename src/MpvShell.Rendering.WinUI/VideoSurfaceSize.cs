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

    /// <summary>
    /// 逻辑宽度 × RasterizationScale，向上取整为物理像素宽度。
    /// </summary>
    public uint PhysicalWidth =>
        (uint)Math.Max(1, (int)Math.Round(LogicalWidth * RasterizationScale));

    /// <summary>
    /// 逻辑高度 × RasterizationScale，向上取整为物理像素高度。
    /// </summary>
    public uint PhysicalHeight =>
        (uint)Math.Max(1, (int)Math.Round(LogicalHeight * RasterizationScale));
}
