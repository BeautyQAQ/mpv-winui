using System.Runtime.InteropServices;
using MpvShell.Player.LibMpv;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>复用生产图形组件的真实 Render API 像素验收，不依赖 WinUI 窗口；CPU 读回仅在测试显式请求时执行。</summary>
internal sealed class MediaFrameProbe : IAsyncDisposable
{
    internal const int Width = 320, Height = 180;
    private readonly MpvPlayerSession _session;
    private readonly RenderWorker _worker;
    private D3D11DeviceManager? _device;
    private CompositionSwapChain? _swapChain;
    private AngleContext? _angle;
    private MpvRenderContext? _render;
    private TaskCompletionSource<byte[]>? _capture;
    private Exception? _failure;
    internal string? Adapter { get; private set; }

    internal MediaFrameProbe(MpvPlayerSession session)
    {
        _session = session;
        _worker = new RenderWorker(Render, error => { _failure = error; _capture?.TrySetException(error); });
    }

    internal Task InitializeAsync(CancellationToken token) => _worker.InvokeAsync(() =>
    {
        _device = new D3D11DeviceManager();
        _device.Initialize();
        _angle = new AngleContext(_device.GetDevicePointer());
        Adapter = _angle.Description;
        _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), Width, Height, Format.B8G8R8A8_UNorm);
        _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
        _render = _session.CreateRenderContext(_angle.GetProcAddress, _worker.Wake);
    }, token);

    internal async Task<byte[]> CaptureAsync(CancellationToken token)
    {
        var capture = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _worker.InvokeAsync(() =>
        {
            if (_failure is not null) throw new InvalidOperationException("图形验收已失败。", _failure);
            if (_capture is not null) throw new InvalidOperationException("不能并发请求像素读回。");
            _capture = capture;
        }, token);
        return await capture.Task.WaitAsync(token);
    }

    private void Render()
    {
        if (_render is null || _failure is not null) return;
        _angle!.MakeBackBufferCurrent();
        if (!_render.Update() && _capture is null) return;
        _render.Render(0, Width, Height, false, _angle.GlInternalFormat, _angle.ColorDepth);
        _angle.PreparePresent();
        byte[]? pixels = _capture is null ? null : ReadPixels();
        _swapChain!.Present();
        _render.ReportSwap();
        if (pixels is not null) { _capture!.TrySetResult(pixels); _capture = null; }
    }

    private byte[] ReadPixels()
    {
        using var buffer = _swapChain!.GetBackBuffer();
        var description = buffer.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device!.GetDevice().CreateTexture2D(description);
        var context = _device.GetImmediateContext();
        context.CopyResource(staging, buffer);
        context.Map(staging, 0, MapMode.Read, MapFlags.None, out var mapped).CheckError();
        try
        {
            var pixels = new byte[Width * Height * 4];
            for (var row = 0; row < Height; row++)
                Marshal.Copy(mapped.DataPointer + checked((int)(row * mapped.RowPitch)), pixels, row * Width * 4, Width * 4);
            return pixels;
        }
        finally { context.Unmap(staging, 0); }
    }

    internal static int ChangedPixels(byte[] first, byte[] second, int top = 0, int bottom = Height)
    {
        var changed = 0;
        for (var index = top * Width * 4; index < bottom * Width * 4; index += 4)
            if (Math.Abs(first[index] - second[index]) + Math.Abs(first[index + 1] - second[index + 1]) +
                Math.Abs(first[index + 2] - second[index + 2]) > 30) changed++;
        return changed;
    }

    public async ValueTask DisposeAsync()
    {
        // 与其他原生测试一致，停止媒体后先归还 render context，再销毁 session。
        try
        {
            await _worker.InvokeAsync(() =>
            {
                try { _angle?.MakeParkingCurrent(); _render?.Dispose(); }
                finally { _render = null; _angle?.Dispose(); _swapChain?.Dispose(); _device?.Dispose(); }
            });
        }
        finally { await _worker.DisposeAsync(); }
    }
}
