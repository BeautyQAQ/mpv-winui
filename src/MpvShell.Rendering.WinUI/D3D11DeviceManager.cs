// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 管理 D3D11 设备、DXGI 适配器和工厂的生命周期。
/// P0-06：不依赖 libmpv，仅用于创建 Composition SwapChain 验证。
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
        if (D3D11.SdkLayersAvailable())
        {
            creationFlags |= DeviceCreationFlags.Debug;
        }
#endif

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

        if (D3D11.D3D11CreateDevice(
            IntPtr.Zero, // 默认适配器
            DriverType.Hardware,
            creationFlags,
            featureLevels,
            out tempDevice,
            out _,
            out tempContext).Failure)
        {
            // 回退到 WARP 设备。
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

        Debug.WriteLine("[D3D11DeviceManager] D3D11 设备已初始化");
    }

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
