namespace MpvShell.Rendering.WinUI;

/// <summary>显示器当前的系统输出状态；宽色域模式不等于 HDR。</summary>
public enum DisplayOutputColorKind
{
    Unknown,
    StandardDynamicRange,
    WideColorGamut,
    HighDynamicRange,
}

/// <summary>
/// Windows 对当前窗口所在显示器的能力快照。亮度来自系统，缺失或无效时为 null，
/// 不把媒体元数据、渲染配置或默认亮度当作显示器的实测能力。
/// </summary>
public sealed record DisplayOutputCapabilities
{
    public string DisplayName { get; init; } = "未知显示器";
    public bool IsAvailable { get; init; }
    public bool IsHdrSupported { get; init; }
    public DisplayOutputColorKind ColorKind { get; init; }
    public bool IsHdrEnabled => IsAvailable && ColorKind == DisplayOutputColorKind.HighDynamicRange;
    public double? SdrWhiteLevelInNits { get; init; }
    public double? PeakLuminanceInNits { get; init; }
    public double? MinLuminanceInNits { get; init; }
    public double? FullFrameLuminanceInNits { get; init; }
    public string? Diagnostic { get; init; }

    public static DisplayOutputCapabilities Unknown { get; } = new();

    internal static DisplayOutputCapabilities FromSystem(
        string displayName,
        DisplayOutputColorKind colorKind,
        bool isHdrSupported,
        double sdrWhiteLevelInNits,
        double peakLuminanceInNits,
        double minLuminanceInNits,
        double fullFrameLuminanceInNits) => new()
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "未知显示器" : displayName,
        IsAvailable = colorKind != DisplayOutputColorKind.Unknown,
        ColorKind = colorKind,
        IsHdrSupported = isHdrSupported,
        SdrWhiteLevelInNits = PositiveLuminance(sdrWhiteLevelInNits),
        PeakLuminanceInNits = PositiveLuminance(peakLuminanceInNits),
        MinLuminanceInNits = double.IsFinite(minLuminanceInNits) && minLuminanceInNits >= 0
            ? minLuminanceInNits : null,
        FullFrameLuminanceInNits = PositiveLuminance(fullFrameLuminanceInNits),
    };

    private static double? PositiveLuminance(double value) =>
        double.IsFinite(value) && value > 0 ? value : null;
}
