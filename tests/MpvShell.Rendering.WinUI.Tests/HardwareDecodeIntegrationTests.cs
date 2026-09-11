using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv;
using MpvShell.Rendering.WinUI.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>显式素材驱动的真实 4K 验收；缺少素材时由 xUnit 明确报告跳过。</summary>
[Collection("HardwareMedia")]
public sealed class HardwareDecodeIntegrationTests
{
    [HardwareMediaFact("MPVSHELL_TEST_HEVC_MEDIA")]
    [Trait("Category", "HardwareMedia")]
    public Task Hevc_main10_should_render_4k_frames_using_gpu_resident_d3d11_decoding() =>
        VerifyMediaAsync("MPVSHELL_TEST_HEVC_MEDIA", "hevc", requireHardware: true);

    [HardwareMediaFact("MPVSHELL_TEST_AV1_MEDIA")]
    [Trait("Category", "HardwareMedia")]
    public Task Av1_should_render_4k_frames_and_report_actual_hardware_or_software_fallback() =>
        VerifyMediaAsync("MPVSHELL_TEST_AV1_MEDIA", "av1", requireHardware: false);

    private static async Task VerifyMediaAsync(string mediaVariable, string codec, bool requireHardware)
    {
        var path = Environment.GetEnvironmentVariable(mediaVariable)!;
        File.Exists(path).Should().BeTrue($"{mediaVariable} 必须指向存在的固定 4K 非全黑测试素材");
        var report = new HardwareMediaReport { Codec = codec, MediaFile = Path.GetFileName(path) };
        using (var file = File.OpenRead(path)) report.MediaSha256 = Convert.ToHexString(SHA256.HashData(file));
        var nativePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libmpv-2.dll");
        using (var file = File.OpenRead(nativePath)) report.MpvSha256 = Convert.ToHexString(SHA256.HashData(file));
        using var process = Process.GetCurrentProcess();
        var initialCpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var reportDirectory = Environment.GetEnvironmentVariable("MPVSHELL_TEST_HARDWARE_REPORT_DIR") ??
            Path.Combine(AppContext.BaseDirectory, "hardware-reports");
        Directory.CreateDirectory(reportDirectory);
        using var trace = new TextWriterTraceListener(new StreamWriter(Path.Combine(reportDirectory, $"{codec}-native.log"), append: false));
        Trace.Listeners.Add(trace);
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" },
            Environment.GetEnvironmentVariable("MPVSHELL_TEST_NATIVE_LOG_LEVEL") ?? "info");
        await using var backend = new LibMpvBackend(session);
        var backendFailure = ObserveFailureAsync(backend, cancellation.Token);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            await using var graphics = new HardwareFrameProbe(session);
            await graphics.InitializeAsync(cancellation.Token);
            report.Adapter = graphics.Adapter;
            await backend.LoadUrlAsync(path, cancellation.Token);
            await graphics.BeginCaptureAsync(cancellation.Token);
            var framesReady = graphics.WaitForFramesAsync(cancellation.Token);
            if (await Task.WhenAny(framesReady, backendFailure) == backendFailure)
                throw new InvalidOperationException(await backendFailure);
            report.PresentedFrames = await framesReady;
            // 真实样片可以以黑场开头（LG OLED Art 约 1.82 秒）；60 帧是连续呈现门槛，
            // 不是非黑画面的到达期限。保持有限等待，全黑/冻结黑帧仍必须失败。
            var imageWait = Stopwatch.StartNew();
            do
            {
                report.NonBlackPixelObserved = await graphics.HasNonBlackPixelAsync(cancellation.Token);
                if (report.NonBlackPixelObserved) break;
                if (backendFailure.IsCompleted) throw new InvalidOperationException(await backendFailure);
                await Task.Delay(100, cancellation.Token);
            } while (imageWait.Elapsed < TimeSpan.FromSeconds(10));
            report.PresentedFrames = await graphics.GetPresentedFramesAsync(cancellation.Token);
            report.PresentedFrames.Should().BeGreaterThanOrEqualTo(60);
            report.NonBlackPixelObserved.Should().BeTrue("真实解码帧必须到达 3840×2160 的 DXGI 后备缓冲");

            InfoPanelSnapshot info;
            do
            {
                info = await backend.GetInfoSnapshotAsync(cancellation.Token);
                if (info.DecodeDiagnostics?.Mode is not (null or VideoDecodeMode.Unknown) && info.Resolution is not null) break;
                await Task.Delay(20, cancellation.Token);
            } while (true);
            report.Info = info;
            report.HardwareVerified = info.DecodeDiagnostics!.IsGpuResident;
            report.SoftwareFallback = info.DecodeDiagnostics.Mode == VideoDecodeMode.Software;
            info.Resolution.Should().Be("3840 × 2160");
            info.VideoCodec.Should().ContainEquivalentOf(codec);
            info.DecodeDiagnostics.Mode.Should().NotBe(VideoDecodeMode.CopyBack);
            if (requireHardware)
            {
                info.BitDepth.Should().Be("10 bit", "HEVC 素材必须覆盖 Main10/P010");
                report.HardwareVerified.Should().BeTrue("HEVC Main10 验收必须实际使用 D3D11 EGL GPU 纹理传递");
                info.DecodeDiagnostics.HardwarePixelFormat.Should().Be("p010");
            }
            else
            {
                (report.HardwareVerified || report.SoftwareFallback).Should().BeTrue("无 AV1 硬解能力的设备必须明确回退软件解码");
            }
            // 验收数据已经取快照；先停止媒体再释放视频上下文，避免清理产生 VO 重建错误日志。
            await session.CommandAsync(["stop"], cancellation.Token);
            while (await session.GetPropertyAsync("idle-active", cancellation.Token) is not true)
                await Task.Delay(20, cancellation.Token);
            report.Status = "通过";
        }
        catch (Exception error)
        {
            report.Status = "失败";
            report.Error = error.Message;
            throw;
        }
        finally
        {
            report.ElapsedSeconds = clock.Elapsed.TotalSeconds;
            report.CpuSeconds = (process.TotalProcessorTime - initialCpu).TotalSeconds;
            report.WorkingSetBytes = process.WorkingSet64;
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, $"{codec}-hardware-report.json"), JsonSerializer.Serialize(report, options));
            trace.Flush();
            Trace.Listeners.Remove(trace);
            cancellation.Cancel();
            try { await backendFailure; }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<string> ObserveFailureAsync(LibMpvBackend backend, CancellationToken cancellationToken)
    {
        await foreach (var change in backend.ObserveEventsAsync(cancellationToken))
            if (change is BackendFaulted failure) return failure.Message;
        return "播放器会话在完成原生媒体验收前关闭。";
    }

    private sealed class HardwareMediaReport
    {
        public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;
        public required string Codec { get; init; }
        public required string MediaFile { get; init; }
        public string? MediaSha256 { get; set; }
        public string? MpvSha256 { get; set; }
        public string? Adapter { get; set; }
        public string Status { get; set; } = "未完成";
        public bool HardwareVerified { get; set; }
        public bool SoftwareFallback { get; set; }
        public int PresentedFrames { get; set; }
        public bool NonBlackPixelObserved { get; set; }
        public double ElapsedSeconds { get; set; }
        public double CpuSeconds { get; set; }
        public long WorkingSetBytes { get; set; }
        public InfoPanelSnapshot? Info { get; set; }
        public string? Error { get; set; }
    }

    private sealed class HardwareFrameProbe : IAsyncDisposable
    {
        private readonly MpvPlayerSession _session;
        private readonly RenderWorker _worker;
        private readonly TaskCompletionSource<int> _framesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private D3D11DeviceManager? _device;
        private CompositionSwapChain? _swapChain;
        private AngleContext? _angle;
        private MpvRenderContext? _render;
        private int _presented;
        private bool _capturing;

        public HardwareFrameProbe(MpvPlayerSession session)
        {
            _session = session;
            _worker = new RenderWorker(Render, error => _framesReady.TrySetException(error));
        }

        public string Adapter { get; private set; } = "未知";

        public Task InitializeAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _device = new D3D11DeviceManager();
            _device.Initialize();
            _angle = new AngleContext(_device.GetDevicePointer());
            Adapter = _angle.Description;
            var getDisplay = Marshal.GetDelegateForFunctionPointer<GetCurrentDisplay>(_angle.GetProcAddress("eglGetCurrentDisplay"));
            var clientExtensions = Marshal.PtrToStringUTF8(AngleNative.QueryString(0, AngleNative.Extensions)) ?? "";
            var displayExtensions = Marshal.PtrToStringUTF8(AngleNative.QueryString(getDisplay(), AngleNative.Extensions)) ?? "";
            var glExtensions = Marshal.PtrToStringUTF8(AngleNative.GlGetString(0x1F03)) ?? "";
            Trace.WriteLine($"[硬解探测] client device_query={clientExtensions.Contains("EGL_EXT_device_query", StringComparison.Ordinal)}; " +
                $"display device_query={displayExtensions.Contains("EGL_EXT_device_query", StringComparison.Ordinal)}; " +
                $"EGLStream={displayExtensions.Contains("EGL_ANGLE_stream_producer_d3d_texture", StringComparison.Ordinal)}; " +
                $"share_handle={displayExtensions.Contains("EGL_ANGLE_d3d_share_handle_client_buffer", StringComparison.Ordinal)}; " +
                $"external_essl3={glExtensions.Contains("GL_OES_EGL_image_external_essl3", StringComparison.Ordinal)}");
            _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), 3840, 2160, Format.B8G8R8A8_UNorm);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _render = _session.CreateRenderContext(_angle.GetProcAddress, _worker.Wake);
        }, cancellationToken);

        public Task<int> WaitForFramesAsync(CancellationToken cancellationToken) => _framesReady.Task.WaitAsync(cancellationToken);

        public async Task<int> GetPresentedFramesAsync(CancellationToken cancellationToken)
        {
            var count = 0;
            await _worker.InvokeAsync(() => count = _presented, cancellationToken);
            return count;
        }

        public Task BeginCaptureAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _presented = 0;
            _capturing = true;
        }, cancellationToken);

        private void Render()
        {
            if (_render is null || _framesReady.Task.IsFaulted) return;
            _angle!.MakeBackBufferCurrent();
            if (!_render.Update()) return;
            _render.Render(0, 3840, 2160, false, _angle.GlInternalFormat, _angle.ColorDepth);
            _angle.PreparePresent();
            _swapChain!.Present();
            _render.ReportSwap();
            if (_capturing && ++_presented >= 60) _framesReady.TrySetResult(_presented);
        }

        public async Task<bool> HasNonBlackPixelAsync(CancellationToken cancellationToken)
        {
            var nonBlack = false;
            await _worker.InvokeAsync(() =>
            {
                // 仅验收等待画面时读回；生产路径与前 60 帧不经过 CPU 回读。
                _angle!.MakeBackBufferCurrent();
                _render!.Render(0, 3840, 2160, false, _angle.GlInternalFormat, _angle.ColorDepth);
                _angle.PreparePresent();
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
                    for (var row = 1; row < 32; row++)
                    for (var column = 1; column < 32; column++)
                    {
                        var pointer = mapped.DataPointer + checked((int)(row * description.Height / 32 * mapped.RowPitch + column * description.Width / 32 * 4));
                        if (Marshal.ReadByte(pointer) + Marshal.ReadByte(pointer, 1) + Marshal.ReadByte(pointer, 2) > 20) nonBlack = true;
                    }
                }
                finally { context.Unmap(staging, 0); }
            }, cancellationToken);
            return nonBlack;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _worker.InvokeAsync(() =>
                {
                    try { _angle?.MakeParkingCurrent(); _render?.Dispose(); }
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

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint GetCurrentDisplay();
    }
}

[CollectionDefinition("HardwareMedia", DisableParallelization = true)]
public sealed class HardwareMediaCollection { }

public sealed class HardwareMediaFactAttribute : FactAttribute
{
    public HardwareMediaFactAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
            Skip = $"设置 {environmentVariable} 后运行真实 4K 原生媒体验收。";
    }
}
