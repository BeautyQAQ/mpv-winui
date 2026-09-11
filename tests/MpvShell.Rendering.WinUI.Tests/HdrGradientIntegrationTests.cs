using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>固定无损 PQ 灰阶的实际解码→GL→DXGI 像素验收，不需要 HDR 显示器。</summary>
[Collection("HardwareMedia")]
public sealed class HdrGradientIntegrationTests
{
    private static readonly int[] SourceCodes = [64, 77, 104, 167, 281, 450, 509, 573, 636, 674, 723];

    [HardwareMediaFact("MPVSHELL_TEST_HDR_GRADIENT_MEDIA")]
    [Trait("Category", "HardwareMedia")]
    public async Task Actual_pq_video_should_preserve_absolute_levels_and_tone_map_without_raised_black_or_clipped_whites()
    {
        var path = Environment.GetEnvironmentVariable("MPVSHELL_TEST_HDR_GRADIENT_MEDIA")!;
        File.Exists(path).Should().BeTrue("先用 prepare-hdr-4k-media.ps1 生成固定无损灰阶");
        var report = new GradientReport { MediaFile = Path.GetFileName(path) };
        using (var file = File.OpenRead(path)) report.MediaSha256 = Convert.ToHexString(SHA256.HashData(file));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            // FFV1 源只有 PQ/BT2020 标签、没有峰值元数据；10000nit 禁止隐含色调映射，
            // 从而能直接比较 limited-range Y' 与 full-range RGB 的 ST2084 绝对编码。
            await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 10000, cancellation.Token);
            await using var graphics = new GradientProbe(session);
            await graphics.InitializeAsync(cancellation.Token);
            report.Adapter = graphics.Adapter;
            var firstFrame = graphics.CaptureAsync(cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            report.Passthrough = await firstFrame;
            await backend.PauseAsync(cancellation.Token);

            InfoPanelSnapshot info;
            do
            {
                info = await backend.GetInfoSnapshotAsync(cancellation.Token);
                if (info.DynamicRange == VideoDynamicRange.Pq && info.Resolution is not null) break;
                await Task.Delay(20, cancellation.Token);
            } while (true);
            report.SourceInfo = info;
            info.Resolution.Should().Be("3840 × 2160");
            info.BitDepth.Should().Be("10 bit");
            // video-codec 是显示长名称（FFV1 在此构建为“FFmpeg video codec #1”）；
            // 容器/codec 身份由固定素材验证脚本确认，像素测试不依赖展示字符串。
            for (var patch = 0; patch < SourceCodes.Length; patch++)
            {
                var expectedCode = (SourceCodes[patch] - 64.0) * 1023 / 876;
                var actual = report.Passthrough.Patches[patch];
                actual.Red.Should().BeApproximately(expectedCode, 2, $"第 {patch} 块必须保留源 PQ 绝对亮度");
                actual.Green.Should().BeApproximately(expectedCode, 2);
                actual.Blue.Should().BeApproximately(expectedCode, 2);
            }
            report.Passthrough.RampUniqueRedCodes.Should().BeGreaterThan(500,
                "源渐变有660档，不得在渲染链提前降低为8bit");

            await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 1000, cancellation.Token);
            report.ToneMappedHdr = await graphics.CaptureAsync(cancellation.Token);
            AssertMonotonic(report.ToneMappedHdr.Patches);
            report.ToneMappedHdr.Patches[0].Red.Should().BeLessThanOrEqualTo(1);
            report.ToneMappedHdr.Patches[^1].Red.Should().BeLessThanOrEqualTo(PqEncode(1000) * 1023 + 2);
            report.ToneMappedHdr.Patches[^1].Red.Should().BeGreaterThan(PqEncode(203) * 1023,
                "映射至1000nit目标后仍应保留超出SDR参考白的高光");

            await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Sdr, 203, cancellation.Token);
            await graphics.SwitchToSdrAsync(cancellation.Token);
            report.ToneMappedSdr = await graphics.CaptureAsync(cancellation.Token);
            AssertMonotonic(report.ToneMappedSdr.Patches);
            report.ToneMappedSdr.Patches[0].Red.Should().BeLessThanOrEqualTo(1, "HDR转SDR不得抬高黑位");
            report.ToneMappedSdr.Patches[^1].Red.Should().BeInRange(128, 255);
            for (var patch = 5; patch < SourceCodes.Length; patch++)
                report.ToneMappedSdr.Patches[patch].Red.Should().BeGreaterThan(
                    report.ToneMappedSdr.Patches[patch - 1].Red, "50至1000nit高光不能被统一裁成纯白");
            report.Status = "通过";
        }
        catch (Exception error)
        {
            report.Status = "失败";
            report.Error = error.ToString();
            throw;
        }
        finally
        {
            var directory = Environment.GetEnvironmentVariable("MPVSHELL_TEST_HARDWARE_REPORT_DIR") ??
                Path.Combine(AppContext.BaseDirectory, "hardware-reports");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "hdr-gradient-pixel-report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void AssertMonotonic(Pixel[] patches)
    {
        for (var index = 1; index < patches.Length; index++)
            patches[index].Red.Should().BeGreaterThanOrEqualTo(patches[index - 1].Red);
    }

    private static double PqEncode(double nits)
    {
        var linear = Math.Pow(nits / 10000, 2610.0 / 16384);
        return Math.Pow((3424.0 / 4096 + 2413.0 / 128 * linear) / (1 + 2392.0 / 128 * linear), 2523.0 / 32);
    }

    private static double PqDecode(double code)
    {
        var nonlinear = Math.Pow(code / 1023, 32.0 / 2523);
        return 10000 * Math.Pow(Math.Max(nonlinear - 3424.0 / 4096, 0) / (2413.0 / 128 - 2392.0 / 128 * nonlinear), 16384.0 / 2610);
    }

    private sealed record Pixel(double Red, double Green, double Blue);
    private sealed record FrameSamples(string Format, Pixel[] Patches, double[]? DecodedPqNits, int RampUniqueRedCodes);
    private sealed class GradientReport
    {
        public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;
        public required string MediaFile { get; init; }
        public string? MediaSha256 { get; set; }
        public string? Adapter { get; set; }
        public string Status { get; set; } = "未完成";
        public string? Error { get; set; }
        public InfoPanelSnapshot? SourceInfo { get; set; }
        public FrameSamples? Passthrough { get; set; }
        public FrameSamples? ToneMappedHdr { get; set; }
        public FrameSamples? ToneMappedSdr { get; set; }
    }

    private sealed class GradientProbe : IAsyncDisposable
    {
        private const int Width = 3840, Height = 2160;
        private readonly MpvPlayerSession _session;
        private readonly RenderWorker _worker;
        private D3D11DeviceManager? _device;
        private CompositionSwapChain? _swapChain;
        private AngleContext? _angle;
        private MpvRenderContext? _render;
        private Format _format = Format.R10G10B10A2_UNorm;
        private TaskCompletionSource<FrameSamples>? _capture;
        private bool _forceFrame;
        private Exception? _failure;

        public GradientProbe(MpvPlayerSession session)
        {
            _session = session;
            _worker = new RenderWorker(Render, error => { _failure = error; _capture?.TrySetException(error); });
        }

        public string Adapter { get; private set; } = "未知";

        public Task InitializeAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _device = new D3D11DeviceManager();
            _device.Initialize();
            _angle = new AngleContext(_device.GetDevicePointer());
            Adapter = _angle.Description;
            _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), Width, Height, _format);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _render = _session.CreateRenderContext(_angle.GetProcAddress, _worker.Wake);
        }, cancellationToken);

        public async Task<FrameSamples> CaptureAsync(CancellationToken cancellationToken)
        {
            var capture = new TaskCompletionSource<FrameSamples>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _worker.InvokeAsync(() =>
            {
                if (_failure is not null) throw new InvalidOperationException("原生帧捕获此前已失败。", _failure);
                _capture = capture;
                _forceFrame = true;
            }, cancellationToken);
            return await capture.Task.WaitAsync(cancellationToken);
        }

        public Task SwitchToSdrAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _angle!.ReleaseBackBuffer();
            _swapChain!.Dispose();
            _format = Format.B8G8R8A8_UNorm;
            _swapChain = new CompositionSwapChain(_device!.GetFactory(), _device.GetDevice(), Width, Height, _format);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
        }, cancellationToken);

        private void Render()
        {
            if (_render is null || _failure is not null) return;
            _angle!.MakeBackBufferCurrent();
            var updated = _render.Update();
            if (!updated && !_forceFrame) return;
            _forceFrame = false;
            _render.Render(0, Width, Height, false, _angle.GlInternalFormat, _angle.ColorDepth);
            _angle.PreparePresent();
            if (_capture is not null)
            {
                var samples = ReadPixels();
                if (samples.Patches[^1].Red > 100)
                {
                    _capture.TrySetResult(samples);
                    _capture = null;
                }
            }
            _swapChain!.Present();
            _render.ReportSwap();
        }

        private FrameSamples ReadPixels()
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
                Pixel Read(int x, int y)
                {
                    var pointer = mapped.DataPointer + checked((int)(y * mapped.RowPitch + x * 4));
                    if (_format == Format.B8G8R8A8_UNorm)
                        return new Pixel(Marshal.ReadByte(pointer, 2), Marshal.ReadByte(pointer, 1), Marshal.ReadByte(pointer));
                    var code = unchecked((uint)Marshal.ReadInt32(pointer));
                    return new Pixel(code & 1023, (code >> 10) & 1023, (code >> 20) & 1023);
                }
                var patches = Enumerable.Range(0, 11).Select(index => Read((int)((index + 0.5) * Width / 11), 1800)).ToArray();
                var unique = Enumerable.Range(0, Width).Select(x => Read(x, 540).Red).Distinct().Count();
                var nits = _format == Format.R10G10B10A2_UNorm ? patches.Select(patch => PqDecode(patch.Red)).ToArray() : null;
                return new FrameSamples(_format.ToString(), patches, nits, unique);
            }
            finally { context.Unmap(staging, 0); }
        }

        public async ValueTask DisposeAsync()
        {
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
}
