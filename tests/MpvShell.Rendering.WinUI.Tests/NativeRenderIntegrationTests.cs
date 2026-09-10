using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using MpvShell.Player.LibMpv;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>
/// 真实 libmpv/ANGLE/D3D11 集成测试。仅测试代码通过 staging texture 读回像素；
/// 生产渲染链直接写 Composition SwapChain，不允许 CPU 逐帧读回。
/// </summary>
public sealed class NativeRenderIntegrationTests
{
    [Fact]
    public async Task Real_video_should_keep_top_red_bottom_blue_after_resize_and_render_context_recreation()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-render-" + Guid.NewGuid().ToString("N") + ".y4m");
        WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            // 第二轮复用同一 core，验证 render context / EGL / D3D 链可以顺序销毁重建。
            for (var cycle = 0; cycle < 2; cycle++)
            {
                await using var graphics = new NativeGraphicsProbe(session);
                await graphics.InitializeAsync();
                var firstFrame = graphics.ReadNextFrameAsync(cancellation.Token);
                await backend.LoadUrlAsync(path, cancellation.Token);
                VerifyColors(await firstFrame);
                await backend.PauseAsync(cancellation.Token);
                foreach (var size in new uint[] { 96, 128, 64 })
                    VerifyColors(await graphics.ResizeAndReadAsync(size, cancellation.Token));
            }
        }
        finally
        {
            await backend.DisposeAsync();
            File.Delete(path);
        }
    }

    private static void VerifyColors(FramePixels pixels)
    {
        pixels.Top.Red.Should().BeGreaterThan(180, "源视频上半部为红色，不能上下倒置或变黑");
        pixels.Top.Blue.Should().BeLessThan(70);
        pixels.Bottom.Blue.Should().BeGreaterThan(180, "源视频下半部为蓝色，resize 后仍应正确渲染");
        pixels.Bottom.Red.Should().BeLessThan(70);
    }

    private static void WriteColorVideo(string path)
    {
        const int size = 64;
        using var output = File.Create(path);
        output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W64 H64 F2:1 Ip A1:1 C420jpeg\n"));
        var plane = new byte[size * size * 3 / 2];
        for (var row = 0; row < size; row++)
            plane.AsSpan(row * size, size).Fill(row < size / 2 ? (byte)81 : (byte)41);
        for (var row = 0; row < size / 2; row++)
        {
            plane.AsSpan(size * size + row * size / 2, size / 2).Fill(row < size / 4 ? (byte)90 : (byte)240);
            plane.AsSpan(size * size * 5 / 4 + row * size / 2, size / 2).Fill(row < size / 4 ? (byte)240 : (byte)110);
        }
        for (var frame = 0; frame < 20; frame++)
        {
            output.Write("FRAME\n"u8);
            output.Write(plane);
        }
    }

    private readonly record struct Pixel(byte Blue, byte Green, byte Red);
    private readonly record struct FramePixels(Pixel Top, Pixel Bottom);

    private sealed class NativeGraphicsProbe : IAsyncDisposable
    {
        private readonly MpvPlayerSession _session;
        private readonly RenderWorker _worker;
        private D3D11DeviceManager? _device;
        private CompositionSwapChain? _swapChain;
        private AngleContext? _angle;
        private MpvRenderContext? _render;
        private TaskCompletionSource<FramePixels>? _sample;
        private bool _forceFrame;
        private bool _failed;
        private Exception? _failure;
        private uint _size = 64;

        public NativeGraphicsProbe(MpvPlayerSession session)
        {
            _session = session;
            _worker = new RenderWorker(Render, ex =>
            {
                _failed = true;
                _failure = ex;
                _sample?.TrySetException(ex);
            });
        }

        public Task InitializeAsync() => _worker.InvokeAsync(() =>
        {
            _device = new D3D11DeviceManager();
            _device.Initialize();
            _angle = new AngleContext(_device.GetDevicePointer());
            _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), _size, _size, Format.B8G8R8A8_UNorm);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _render = _session.CreateRenderContext(_angle.GetProcAddress, _worker.Wake);
        });

        public Task<FramePixels> ReadNextFrameAsync(CancellationToken cancellationToken) =>
            RequestFrameAsync(null, cancellationToken);

        public Task<FramePixels> ResizeAndReadAsync(uint size, CancellationToken cancellationToken) =>
            RequestFrameAsync(size, cancellationToken);

        private async Task<FramePixels> RequestFrameAsync(uint? size, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<FramePixels>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _worker.InvokeAsync(() =>
            {
                if (_failed) throw new InvalidOperationException("渲染测试此前已失败。", _failure);
                if (size is { } nextSize)
                {
                    _angle!.ReleaseBackBuffer();
                    _swapChain!.Resize(nextSize, nextSize);
                    _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
                    _size = nextSize;
                }
                _sample = completion;
                _forceFrame = true;
            }, cancellationToken);
            return await completion.Task.WaitAsync(cancellationToken);
        }

        private void Render()
        {
            if (_render is null || _failed) return;
            _angle!.MakeBackBufferCurrent();
            var updated = _render.Update();
            if (!updated && !_forceFrame) return;
            _forceFrame = false;
            _render.Render(0, (int)_size, (int)_size, flipY: false);
            _angle.PreparePresent();
            if (_sample is not null)
            {
                var pixels = ReadPixels();
                // 初始空闲帧可能为黑色；只有实际媒体解码产生颜色后才记录验收帧。
                if (pixels.Top.Red + pixels.Top.Blue > 100 || pixels.Bottom.Red + pixels.Bottom.Blue > 100)
                {
                    _sample.TrySetResult(pixels);
                    _sample = null;
                }
            }
            _swapChain!.Present();
            _render.ReportSwap();
        }

        private FramePixels ReadPixels()
        {
            using var backBuffer = _swapChain!.GetBackBuffer();
            var description = backBuffer.Description;
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CPUAccessFlags = CpuAccessFlags.Read;
            description.MiscFlags = ResourceOptionFlags.None;
            using var staging = _device!.GetDevice().CreateTexture2D(description);
            var context = _device.GetImmediateContext();
            context.CopyResource(staging, backBuffer);
            context.Map(staging, 0, MapMode.Read, MapFlags.None, out var mapped).CheckError();
            try
            {
                Pixel Sample(uint row)
                {
                    var pointer = mapped.DataPointer + checked((int)(row * mapped.RowPitch + _size / 2 * 4));
                    return new Pixel(Marshal.ReadByte(pointer), Marshal.ReadByte(pointer, 1), Marshal.ReadByte(pointer, 2));
                }
                return new FramePixels(Sample(_size / 4), Sample(_size * 3 / 4));
            }
            finally { context.Unmap(staging, 0); }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _worker.InvokeAsync(() =>
                {
                    try
                    {
                        _angle?.MakeParkingCurrent();
                        _render?.Dispose();
                    }
                    finally
                    {
                        _render = null;
                        _angle?.Dispose();
                        _swapChain?.Dispose();
                        _device?.Dispose();
                    }
                });
            }
            finally { await _worker.DisposeAsync(); }
        }
    }
}
