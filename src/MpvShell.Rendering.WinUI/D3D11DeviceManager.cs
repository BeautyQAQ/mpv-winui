// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 管理 D3D11 设备、DXGI 适配器和工厂的生命周期。
/// 设备由 ANGLE 和 Composition SwapChain 共享，访问限制在渲染线程。
/// </summary>
internal sealed class D3D11DeviceManager : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _immediateContext;
    private IDXGIDevice? _dxgiDevice;
    private IDXGIAdapter? _adapter;
    private IDXGIFactory2? _dxgiFactory;

    /// <summary>
    /// 初始化 D3D11 设备及关联的 DXGI 资源。
    /// </summary>
    public void Initialize()
    {
        if (_device is not null)
            return;

        // 创建 D3D11 设备（使用硬件适配器）。
        var creationFlags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        // 调试层会校验 ANGLE 发出的每一次 D3D 调用，对 4K 逐帧渲染的吞吐影响很大。
        // Debug 构建默认保留它以发现资源错误；性能对照运行可用 MPVSHELL_D3D11_DEBUG_LAYER=0 关闭，
        // 以便在同一构建配置下区分"托管 Debug 开销"与"调试层开销"。Release 从不启用。
        if (IsDebugLayerRequested(Environment.GetEnvironmentVariable("MPVSHELL_D3D11_DEBUG_LAYER")) && D3D11.SdkLayersAvailable())
        {
            creationFlags |= DeviceCreationFlags.Debug;
        }
#endif
        IsDebugLayerEnabled = (creationFlags & DeviceCreationFlags.Debug) != 0;

        _dxgiFactory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();

        // 尝试使用硬件适配器创建设备，失败时回退到 WARP。
        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        };

        ID3D11Device tempDevice;
        ID3D11DeviceContext tempContext;

        var hardwareResult = D3D11.D3D11CreateDevice(
            IntPtr.Zero, // 默认适配器
            DriverType.Hardware,
            creationFlags,
            featureLevels,
            out tempDevice,
            out _,
            out tempContext);
        if (hardwareResult.Failure)
        {
            // 回退到 WARP 设备。
            Trace.WriteLine($"[D3D11DeviceManager] 硬件设备创建失败：{hardwareResult}；尝试 WARP 软件设备。");
            D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Warp,
                creationFlags,
                featureLevels,
                out tempDevice,
                out _,
                out tempContext).CheckError();
        }

        _device = tempDevice;
        _immediateContext = tempContext;

        // 获取 DXGI 接口。
        _dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _adapter = _dxgiDevice.GetParent<IDXGIAdapter>();

        var description = _adapter.Description;
        AdapterDescription = description.Description;
        IsWarpDevice = hardwareResult.Failure;
        Trace.WriteLine($"[D3D11DeviceManager] D3D11 已初始化；设备 {description.Description}；" +
            $"Vendor=0x{description.VendorId:X4} Device=0x{description.DeviceId:X4}；" +
            $"专用显存 {(ulong)description.DedicatedVideoMemory / (1024 * 1024)} MiB；" +
            $"驱动类型 {(hardwareResult.Failure ? "WARP" : "Hardware")}；创建标志 {creationFlags}；" +
            $"调试层 {(IsDebugLayerEnabled ? "已启用" : "未启用")}。");
    }

    /// <summary>设备创建时是否实际带有 D3D11 调试层；性能记录必须登记该状态。</summary>
    public bool IsDebugLayerEnabled { get; private set; }

    /// <summary>是否回退到了 WARP 软件设备。</summary>
    public bool IsWarpDevice { get; private set; }

    /// <summary>DXGI 适配器描述，例如显卡型号；初始化前为 null。</summary>
    public string? AdapterDescription { get; private set; }

    /// <summary>仅 Debug 构建使用：MPVSHELL_D3D11_DEBUG_LAYER 为 0/no/false/off 时不请求调试层。</summary>
    internal static bool IsDebugLayerRequested(string? environmentValue) =>
        environmentValue?.Trim().ToLowerInvariant() is not ("0" or "no" or "false" or "off");

    /// <summary>
    /// 获取 D3D11 设备（适用于 SwapChain 创建）。
    /// </summary>
    public ID3D11Device GetDevice()
    {
        ObjectDisposedException.ThrowIf(_device is null, this);
        return _device!;
    }

    /// <summary>
    /// 获取 immediate context，用于对 SwapChain 后备缓冲区执行清屏。
    /// </summary>
    public ID3D11DeviceContext GetImmediateContext()
    {
        ObjectDisposedException.ThrowIf(_immediateContext is null, this);
        return _immediateContext!;
    }

    /// <summary>
    /// 获取 D3D11 设备的 IUnknown 指针，供 ISwapChainPanelNative::SetSwapChain 使用。
    /// </summary>
    public IntPtr GetDevicePointer()
    {
        ObjectDisposedException.ThrowIf(_device is null, this);
        return _device!.NativePointer;
    }

    /// <summary>
    /// 获取 IDXGIFactory2 用于创建 Composition SwapChain。
    /// </summary>
    public IDXGIFactory2 GetFactory()
    {
        ObjectDisposedException.ThrowIf(_dxgiFactory is null, this);
        return _dxgiFactory!;
    }

    /// <summary>
    /// 获取 IDXGIAdapter。
    /// </summary>
    public IDXGIAdapter GetAdapter()
    {
        ObjectDisposedException.ThrowIf(_adapter is null, this);
        return _adapter!;
    }

    public void Dispose()
    {
        _dxgiFactory?.Dispose();
        _adapter?.Dispose();
        _dxgiDevice?.Dispose();
        _immediateContext?.Dispose();
        _device?.Dispose();

        _dxgiFactory = null;
        _adapter = null;
        _dxgiDevice = null;
        _immediateContext = null;
        _device = null;
    }
}
