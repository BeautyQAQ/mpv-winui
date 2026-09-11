// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 封装 DXGI Composition SwapChain 的创建、Resize 和 Present。
/// 所有方法均由渲染线程调用，后备缓冲区直接交给 ANGLE。
/// </summary>
internal sealed class CompositionSwapChain : IDisposable
{
    private readonly IDXGISwapChain1? _swapChain;
    private readonly Format _format;
    private bool _disposed;

    /// <summary>
    /// 使用 Composition SwapChain 创建 Composition SwapChain。
    /// </summary>
    public CompositionSwapChain(IDXGIFactory2 factory, ID3D11Device device, uint width, uint height, Format format)
    {
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
            AlphaMode = AlphaMode.Ignore,
        };

        // ID3D11Device 是 SharpGen 的 ComObject，可直接作为 IUnknown 传入。
        _swapChain = factory.CreateSwapChainForComposition(device, desc, null);

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

        // 调用方已经解除 EGL 对后备缓冲区的引用。
        _swapChain.ResizeBuffers(2, width, height, _format, SwapChainFlags.None).CheckError();
        Debug.WriteLine($"[CompositionSwapChain] 调整尺寸：{width}x{height}");
    }

    /// <summary>
    /// 获取一个后备缓冲区引用，调用方负责释放。
    /// </summary>
    public ID3D11Texture2D GetBackBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _swapChain!.GetBuffer<ID3D11Texture2D>(0);
    }

    public void SetScale(double rasterizationScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var swapChain2 = _swapChain!.QueryInterface<IDXGISwapChain2>();
        swapChain2.MatrixTransform = Matrix3x2.CreateScale((float)(1 / rasterizationScale));
    }

    /// <summary>格式本身不声明 PQ；必须确认呈现支持并显式设置与像素编码匹配的色彩空间。</summary>
    public void SetColorSpace(ColorSpaceType colorSpace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var swapChain3 = _swapChain!.QueryInterface<IDXGISwapChain3>();
        var support = swapChain3.CheckColorSpaceSupport(colorSpace);
        if ((support & SwapChainColorSpaceSupportFlags.Present) == 0)
            throw new NotSupportedException($"当前图形输出不支持 {colorSpace} 呈现。");
        swapChain3.SetColorSpace1(colorSpace);
    }

    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _swapChain!.Present(1, PresentFlags.None).CheckError();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _swapChain?.Dispose();
    }
}
