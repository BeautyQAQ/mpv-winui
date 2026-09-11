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
    public async Task Sdr_reference_white_should_encode_as_203_nits_in_native_hdr10_pixels()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-pq-white-" + Guid.NewGuid().ToString("N") + ".y4m");
        WriteColorVideo(path, whiteAndBlack: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 1000, cancellation.Token);
            await using var graphics = new NativeGraphicsProbe(session, Format.R10G10B10A2_UNorm);
            await graphics.InitializeAsync();
            var frame = graphics.ReadNextFrameAsync(cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            var pixels = await frame;
            // ST 2084 绝对亮度编码；固定 SDR 白参考203nit。读回10bit码值，
            // 允许两个量化台阶含输出抖动，避免将“能设置pq选项”等同于正确HDR输出。
            const double m1 = 2610.0 / 16384, m2 = 2523.0 / 32;
            const double c1 = 3424.0 / 4096, c2 = 2413.0 / 128, c3 = 2392.0 / 128;
            var luminance = Math.Pow(203.0 / 10000, m1);
            var encodedWhite = Math.Pow((c1 + c2 * luminance) / (1 + c3 * luminance), m2) * 255;
            pixels.Top.Red.Should().BeApproximately(encodedWhite, 2.0 * 255 / 1023);
            pixels.Top.Green.Should().BeApproximately(encodedWhite, 2.0 * 255 / 1023);
            pixels.Top.Blue.Should().BeApproximately(encodedWhite, 2.0 * 255 / 1023);
            pixels.Bottom.Red.Should().BeLessThanOrEqualTo(255.0 / 1023);
        }
        finally
        {
            await backend.DisposeAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Paused_video_should_survive_sdr_hdr10_surface_switches_without_reloading_or_recreating_the_context()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-format-" + Guid.NewGuid().ToString("N") + ".y4m");
        WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            await using var graphics = new NativeGraphicsProbe(session);
            await graphics.InitializeAsync();
            var firstFrame = graphics.ReadNextFrameAsync(cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            VerifyColors(await firstFrame);
            await backend.PauseAsync(cancellation.Token);

            // 同一 mpv/EGL context、同一已加载暂停帧；只替换实际 DXGI/EGL 表面。
            for (var cycle = 0; cycle < 2; cycle++)
            {
                await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 1000, cancellation.Token);
                var hdr = await graphics.SwitchFormatAndReadAsync(Format.R10G10B10A2_UNorm, cancellation.Token);
                hdr.Top.Red.Should().BeGreaterThan(hdr.Top.Blue + 30, "PQ/Rec2020 转换后上部仍为红色");
                hdr.Bottom.Blue.Should().BeGreaterThan(hdr.Bottom.Red + 30, "保留暂停视频帧，不能退回空白帧");

                await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Sdr, 203, cancellation.Token);
                VerifyColors(await graphics.SwitchFormatAndReadAsync(Format.B8G8R8A8_UNorm, cancellation.Token));
            }
        }
        finally
        {
            await backend.DisposeAsync();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(Format.R10G10B10A2_UNorm)]
    [InlineData(Format.R16G16B16A16_Float)]
    public async Task Imported_high_precision_swapchain_should_preserve_native_pixel_precision(Format format)
    {
        await using var worker = new RenderWorker(() => { }, _ => { });
        await worker.InvokeAsync(() =>
        {
            using var device = new D3D11DeviceManager();
            device.Initialize();
            using var swapChain = new CompositionSwapChain(device.GetFactory(), device.GetDevice(), 16, 16, format);
            using var angle = new AngleContext(device.GetDevicePointer(), format);
            angle.ImportBackBuffer(swapChain.GetBackBuffer());
            if (format == Format.R10G10B10A2_UNorm)
                angle.ClearColor(512f / 1023, 513f / 1023, 514f / 1023, 1);
            else
                angle.ClearColor(2.5f, -0.25f, 0.5005f, 1);
            angle.PreparePresent();

            using var buffer = swapChain.GetBackBuffer();
            var description = buffer.Description;
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CPUAccessFlags = CpuAccessFlags.Read;
            description.MiscFlags = ResourceOptionFlags.None;
            using var staging = device.GetDevice().CreateTexture2D(description);
            var context = device.GetImmediateContext();
            context.CopyResource(staging, buffer);
            context.Map(staging, 0, MapMode.Read, MapFlags.None, out var mapped).CheckError();
            try
            {
                if (format == Format.R10G10B10A2_UNorm)
                {
                    var pixel = unchecked((uint)Marshal.ReadInt32(mapped.DataPointer));
                    (pixel & 1023).Should().Be(512);
                    ((pixel >> 10) & 1023).Should().Be(513);
                    ((pixel >> 20) & 1023).Should().Be(514);
                    angle.ColorDepth.Should().Be(10);
                    angle.GlInternalFormat.Should().Be(0x8059);
                }
                else
                {
                    float Sample(int channel) => (float)BitConverter.UInt16BitsToHalf(
                        unchecked((ushort)Marshal.ReadInt16(mapped.DataPointer, channel * 2)));
                    Sample(0).Should().Be(2.5f, "FP16 存储必须保留超过 1.0 的 HDR 范围");
                    Sample(1).Should().Be(-0.25f, "FP16 存储必须保留 scRGB 扩展色域所需负值");
                    Sample(2).Should().BeApproximately(0.5005f, 0.0003f);
                    angle.ColorDepth.Should().Be(16);
                    angle.GlInternalFormat.Should().Be(0x881A);
                }
            }
            finally { context.Unmap(staging, 0); }
        });
    }

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

    private static void WriteColorVideo(string path, bool whiteAndBlack = false)
    {
        const int size = 64;
        using var output = File.Create(path);
        output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W64 H64 F2:1 Ip A1:1 C420jpeg\n"));
        var plane = new byte[size * size * 3 / 2];
        for (var row = 0; row < size; row++)
            plane.AsSpan(row * size, size).Fill(whiteAndBlack
                ? row < size / 2 ? (byte)235 : (byte)16
                : row < size / 2 ? (byte)81 : (byte)41);
        for (var row = 0; row < size / 2; row++)
        {
            plane.AsSpan(size * size + row * size / 2, size / 2).Fill(whiteAndBlack ? (byte)128 : row < size / 4 ? (byte)90 : (byte)240);
            plane.AsSpan(size * size * 5 / 4 + row * size / 2, size / 2).Fill(whiteAndBlack ? (byte)128 : row < size / 4 ? (byte)240 : (byte)110);
        }
        for (var frame = 0; frame < 20; frame++)
        {
            output.Write("FRAME\n"u8);
            output.Write(plane);
        }
    }

    private readonly record struct Pixel(double Blue, double Green, double Red);
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
        private Format _format = Format.B8G8R8A8_UNorm;

        public NativeGraphicsProbe(MpvPlayerSession session, Format format = Format.B8G8R8A8_UNorm)
        {
            _session = session;
            _format = format;
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
            _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), _size, _size, _format);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _render = _session.CreateRenderContext(_angle.GetProcAddress, _worker.Wake);
        });

        public Task<FramePixels> ReadNextFrameAsync(CancellationToken cancellationToken) =>
            RequestFrameAsync(null, cancellationToken);

        public Task<FramePixels> ResizeAndReadAsync(uint size, CancellationToken cancellationToken) =>
            RequestFrameAsync(size, cancellationToken);

        public async Task<FramePixels> SwitchFormatAndReadAsync(Format format, CancellationToken cancellationToken)
        {
            await _worker.InvokeAsync(() =>
            {
                _angle!.ReleaseBackBuffer();
                _swapChain!.Dispose();
                _swapChain = new CompositionSwapChain(_device!.GetFactory(), _device.GetDevice(), _size, _size, format);
                _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
                _format = format;
            }, cancellationToken);
            return await RequestFrameAsync(null, cancellationToken);
        }

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
            _render.Render(0, (int)_size, (int)_size, flipY: false,
                internalFormat: _angle.GlInternalFormat, depth: _angle.ColorDepth);
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
                    if (_format == Format.R10G10B10A2_UNorm)
                    {
                        var pixel = unchecked((uint)Marshal.ReadInt32(pointer));
                        return new Pixel(((pixel >> 20) & 1023) * 255.0 / 1023,
                            ((pixel >> 10) & 1023) * 255.0 / 1023, (pixel & 1023) * 255.0 / 1023);
                    }
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
