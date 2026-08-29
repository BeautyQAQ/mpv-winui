// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Diagnostics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 封装 DXGI Composition SwapChain 的创建、Resize 和 Present。
/// P0-06：仅用于验证 D3D11 -> Composition SwapChain -> SwapChainPanel 链路。
/// </summary>
internal sealed class CompositionSwapChain : IDisposable
{
    private readonly IDXGISwapChain1? _swapChain;
    private readonly ID3D11Device _device;
    private readonly Format _format;
    private ID3D11RenderTargetView? _renderTargetView;
    private bool _disposed;

    /// <summary>
    /// 使用 Composition SwapChain 创建 Composition SwapChain。
    /// </summary>
    public CompositionSwapChain(IDXGIFactory2 factory, ID3D11Device device, uint width, uint height, Format format)
    {
        _device = device;
        _format = format;

        var desc = new SwapChainDescription1
        {
            Width = width,
            Height = height,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Unspecified,
        };

        // ID3D11Device 是 SharpGen 的 ComObject，可直接作为 IUnknown 传入。
        _swapChain = factory.CreateSwapChainForComposition(device, desc, null);
        CreateRenderTargetView();

        Debug.WriteLine($"[CompositionSwapChain] 已创建：{width}x{height}");
    }

    /// <summary>
    /// 获取 SwapChain 的 IUnknown 指针，供 ISwapChainPanelNative::SetSwapChain 使用。
    /// </summary>
    public IntPtr NativePointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed || _swapChain is null, this);
            return _swapChain!.NativePointer;
        }
    }

    /// <summary>
    /// Resize SwapChain 的缓冲区。
    /// </summary>
    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_swapChain is null)
            return;

        // ResizeBuffers 要求先释放后备缓冲区的所有视图引用。
        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _swapChain.ResizeBuffers(2, width, height, _format, SwapChainFlags.None);
        CreateRenderTargetView();
        Debug.WriteLine($"[CompositionSwapChain] Resized to {width}x{height}");
    }

    /// <summary>
    /// 将后备缓冲区清为确定颜色并 Present 当前帧。
    /// </summary>
    public void ClearAndPresent(ID3D11DeviceContext context, Vortice.Mathematics.Color4 color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_swapChain is null || _renderTargetView is null)
            return;

        context.ClearRenderTargetView(_renderTargetView, color);
        _swapChain.Present(1, PresentFlags.None);
    }

    private void CreateRenderTargetView()
    {
        if (_swapChain is null)
            return;

        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(backBuffer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderTargetView?.Dispose();
        _renderTargetView = null;
        _swapChain?.Dispose();
    }
}
