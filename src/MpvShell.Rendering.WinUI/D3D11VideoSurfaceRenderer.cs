// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using MpvShell.Player.LibMpv;
using PanelSwapChainNative = MpvShell.Rendering.WinUI.Interop.ISwapChainPanelNative;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// IVideoSurfaceRenderer 的 D3D11 实现。
/// P0-06：在不依赖 libmpv 的情况下，仅用测试图案（纯色清屏）Present 来验证
/// D3D11、DXGI Composition SwapChain 和 SwapChainPanel 链路的正确性。
/// </summary>
public sealed class D3D11VideoSurfaceRenderer : IVideoSurfaceRenderer
{
    private D3D11DeviceManager? _deviceManager;
    private CompositionSwapChain? _swapChain;
    private SwapChainPanel? _surface;
    private VideoSurfaceSize _currentSize;
    private bool _initialized;
    private bool _detached;
    private uint _lastPhysicalWidth;
    private uint _lastPhysicalHeight;

    // 测试图案：高饱和亮蓝色，确保能与页面的深色 XAML 背景明确区分。
    private static readonly Color4 ClearColor = new(0.02f, 0.18f, 0.85f, 1.0f);

    public async ValueTask InitializeAsync(IMpvPlayerSession session, CancellationToken cancellationToken)
    {
        // P0-06：不使用 libmpv；只创建 D3D11 设备。
        _deviceManager?.Dispose();
        _deviceManager = new D3D11DeviceManager();
        _deviceManager.Initialize();
        _initialized = true;
        await Task.CompletedTask;
    }

    public async ValueTask AttachAsync(SwapChainPanel surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!_initialized || _deviceManager is null)
        {
            throw new InvalidOperationException("必须先调用 InitializeAsync 才能绑定视频表面。");
        }

        _surface = surface;
        _detached = false;

        var physicalWidth = ComputePhysicalWidth(surface);
        var physicalHeight = ComputePhysicalHeight(surface);

        _swapChain?.Dispose();
        _swapChain = new CompositionSwapChain(
            _deviceManager.GetFactory(),
            _deviceManager.GetDevice(),
            physicalWidth,
            physicalHeight,
            Format.B8G8R8A8_UNorm);

        // 在 UI 线程上绑定 ISwapChainPanelNative。
        var panelNative = WinRT.CastExtensions.As<PanelSwapChainNative>(surface);
        Marshal.ThrowExceptionForHR(panelNative.SetSwapChain(_swapChain.NativePointer));

        // 首帧清屏 Present。
        PresentClearScreen();

        Debug.WriteLine($"[D3D11VideoSurfaceRenderer] 绑定表面：{physicalWidth}x{physicalHeight}");
        await Task.CompletedTask;
    }

    public async ValueTask ResizeAsync(VideoSurfaceSize size, CancellationToken cancellationToken)
    {
        _currentSize = size;

        if (_swapChain is null || _surface is null || _detached)
        {
            return;
        }

        // 连续 resize 合并：只有当物理像素尺寸变化时才真正调整 SwapChain。
        if (size.PhysicalWidth == _lastPhysicalWidth && size.PhysicalHeight == _lastPhysicalHeight)
        {
            return;
        }

        _swapChain.Resize(size.PhysicalWidth, size.PhysicalHeight);
        PresentClearScreen();

        _lastPhysicalWidth = size.PhysicalWidth;
        _lastPhysicalHeight = size.PhysicalHeight;

        Debug.WriteLine($"[D3D11VideoSurfaceRenderer] 调整尺寸：{size.PhysicalWidth}x{size.PhysicalHeight}");
        await Task.CompletedTask;
    }

    public async ValueTask DetachAsync(CancellationToken cancellationToken)
    {
        if (_surface is not null && !_detached)
        {
            // 先设置空 SwapChain，再释放图形资源。
            var panelNative = WinRT.CastExtensions.As<PanelSwapChainNative>(_surface);
            Marshal.ThrowExceptionForHR(panelNative.SetSwapChain(nint.Zero));
            _detached = true;
        }

        _swapChain?.Dispose();
        _swapChain = null;
        _surface = null;

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync(CancellationToken.None);
        _deviceManager?.Dispose();
        _deviceManager = null;
        _initialized = false;
    }

    private void PresentClearScreen()
    {
        if (_swapChain is null || _deviceManager is null)
        {
            return;
        }

        _swapChain.ClearAndPresent(_deviceManager.GetImmediateContext(), ClearColor);
    }

    private static uint ComputePhysicalWidth(SwapChainPanel surface) =>
        (uint)Math.Max(1, (int)Math.Round(surface.ActualWidth * surface.RasterizationScale));

    private static uint ComputePhysicalHeight(SwapChainPanel surface) =>
        (uint)Math.Max(1, (int)Math.Round(surface.ActualHeight * surface.RasterizationScale));
}
