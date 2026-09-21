namespace MpvShell.Rendering.WinUI;

/// <summary>当前渲染设备的环境事实；性能记录用它区分调试层、WARP 与实际显卡，不从文件名或构建配置推断。</summary>
public sealed record RenderDeviceInfo(
    string? Adapter,
    bool D3D11DebugLayerEnabled,
    bool IsWarpDevice,
    string? AngleRenderer,
    string OutputFormat,
    uint SurfaceWidth,
    uint SurfaceHeight);
