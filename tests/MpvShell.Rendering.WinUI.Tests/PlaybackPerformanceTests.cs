using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>
/// P1-01 的自然播放性能测量。它与功能验收分开：稳态窗口内不做 CPU 像素读回、不强制重绘，
/// 只在渲染线程按 mpv 的帧节奏 Render/Present 并累计耗时；mpv 的呈现/解码丢帧计数取窗口首尾差值。
/// 默认不对吞吐设通过断言，只输出报告；设置 MPVSHELL_TEST_PERF_MIN_FPS 后才按该阈值判定。
/// 参数全部来自环境变量（见 <see cref="PerformanceScenario.FromEnvironment"/>），
/// 使同一素材可以在 Debug/Release、有无调试层、有无读回采样、vsync 0/1 之间做同条件对照。
/// 离屏 Composition SwapChain 没有绑定到可见视觉；Present 是否受显示器 vblank 节流以报告中的
/// Present 耗时分布为准，不能替代真实窗口内的应用测量（见 test-app-recovery.ps1 的 performance 模式）。
/// </summary>
[Collection("HardwareMedia")]
public sealed class PlaybackPerformanceTests
{
    [HardwareMediaFact("MPVSHELL_TEST_PERF_MEDIA")]
    [Trait("Category", "HardwareMedia")]
    [Trait("Category", "Performance")]
    public async Task Natural_playback_throughput_should_be_measured_and_reported_separately_from_functional_checks()
    {
        var scenario = PerformanceScenario.FromEnvironment();
        var report = await RunAsync(scenario);

        // 功能断言：媒体确实在稳态窗口内连续播放、解码模式已知、窗口结束后画面非黑。
        var expectedWindow = Math.Min(scenario.SteadySeconds, report.MediaDurationSeconds - 1 - scenario.WarmupSeconds);
        report.Steady.PositionAdvancedSeconds.Should().BeGreaterThanOrEqualTo(expectedWindow - 0.5,
            "稳态窗口必须覆盖连续播放，否则吞吐数字没有意义");
        report.Info!.DecodeDiagnostics!.Mode.Should().NotBe(VideoDecodeMode.Unknown);
        report.NonBlackPixelObserved.Should().BeTrue("窗口结束后的一次读回必须看到真实画面");
        if (scenario.Hwdec == "d3d11va" && scenario.RequireHardwareDecode)
            report.Info.DecodeDiagnostics.IsGpuResident.Should().BeTrue("要求硬解的场景必须实际走 D3D11 EGL GPU 纹理传递");

        // 性能判定只在场景显式给出阈值时执行；否则报告保持"未判定"，由证据文档按场景规则解读。
        if (scenario.MinimumPresentedFps is { } minimum)
            report.Steady.Statistics.PresentedFramesPerSecond.Should().BeGreaterThanOrEqualTo(minimum,
                $"场景 {scenario.Label} 的稳态自然呈现帧率低于判定阈值");
    }

    /// <summary>不依赖外部素材的测量链路自检：验证窗口、计数、读回采样和报告写出都能工作，不判定吞吐。</summary>
    [Fact]
    public async Task Measurement_harness_should_run_on_generated_media_without_asserting_throughput()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-perf-harness-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        try
        {
            var scenario = new PerformanceScenario
            {
                MediaPath = path, Label = "harness-selfcheck", Output = "sdr", SurfaceWidth = 256, SurfaceHeight = 144,
                WarmupSeconds = 1, SteadySeconds = 4, Hwdec = "no", SyncInterval = 0, ReadbackIntervalSeconds = 1,
                RequireHardwareDecode = false,
            };
            var report = await RunAsync(scenario);
            report.Steady.Statistics.PresentedFrames.Should().BeGreaterThanOrEqualTo(5, "2 fps 素材在 4 秒窗口内应至少呈现 5 帧");
            report.Steady.ReadbackSamples.Should().BeGreaterThanOrEqualTo(2);
            report.Steady.Statistics.Render.Count.Should().Be(report.Steady.Statistics.PresentedFrames);
            report.Steady.VoDroppedFrames.Should().Be(0, "2 fps 的 64×64 素材不应出现呈现丢帧");
            report.NonBlackPixelObserved.Should().BeTrue();
            File.Exists(report.ReportPath!).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    private static async Task<PerformanceReport> RunAsync(PerformanceScenario scenario)
    {
        File.Exists(scenario.MediaPath).Should().BeTrue("性能场景必须指向存在的固定素材");
        var reportDirectory = Environment.GetEnvironmentVariable("MPVSHELL_TEST_HARDWARE_REPORT_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "hardware-reports");
        Directory.CreateDirectory(reportDirectory);
        var report = new PerformanceReport { Scenario = scenario, MediaFile = Path.GetFileName(scenario.MediaPath) };
        using (var file = File.OpenRead(scenario.MediaPath)) report.MediaSha256 = Convert.ToHexString(SHA256.HashData(file));
        var nativePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libmpv-2.dll");
        using (var file = File.OpenRead(nativePath)) report.MpvSha256 = Convert.ToHexString(SHA256.HashData(file));
        var reportPath = Path.Combine(reportDirectory, $"performance-{scenario.Label}.json");
        report.ReportPath = reportPath;

        using var process = Process.GetCurrentProcess();
        var initialCpu = process.TotalProcessorTime;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(scenario.WarmupSeconds + scenario.SteadySeconds + 90));
        using var trace = new TextWriterTraceListener(new StreamWriter(Path.Combine(reportDirectory, $"performance-{scenario.Label}.log"), append: false));
        Trace.Listeners.Add(trace);
        var options = new Dictionary<string, string> { ["ao"] = "null", ["hwdec"] = scenario.Hwdec };
        await using var session = new MpvPlayerSession(options, Environment.GetEnvironmentVariable("MPVSHELL_TEST_NATIVE_LOG_LEVEL") ?? "info");
        await using var backend = new LibMpvBackend(session);
        var backendFailure = ObserveFailureAsync(backend, cancellation.Token);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            if (scenario.Output == "hdr")
                await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, scenario.PeakLuminance, cancellation.Token);
            await using var probe = new PerformanceProbe(session, scenario);
            await probe.InitializeAsync(cancellation.Token);
            report.Device = probe.DeviceInfo;
            await backend.LoadUrlAsync(scenario.MediaPath, cancellation.Token);

            // 预热：等待媒体位置越过预热秒数；期间的首帧、着色器编译和解码器初始化不计入稳态。
            await WaitForPositionAsync(session, backendFailure, scenario.WarmupSeconds, cancellation.Token);
            report.MediaDurationSeconds = Convert.ToDouble(await session.GetPropertyAsync("duration", cancellation.Token) ?? 0d, CultureInfo.InvariantCulture);
            var window = report.Steady;
            window.PositionStartSeconds = await PositionAsync(session, cancellation.Token);
            window.CountersAtStart = await MpvCountersAsync(session, cancellation.Token);
            await probe.BeginWindowAsync(cancellation.Token);
            var clock = Stopwatch.StartNew();
            var nextReadback = scenario.ReadbackIntervalSeconds > 0 ? scenario.ReadbackIntervalSeconds : double.PositiveInfinity;
            // 窗口不越过片尾前 1 秒，保证窗口结束后仍有帧可供一次读回确认画面。
            var endAt = Math.Min(scenario.WarmupSeconds + scenario.SteadySeconds,
                report.MediaDurationSeconds > 0 ? report.MediaDurationSeconds - 1 : double.PositiveInfinity);
            while (true)
            {
                if (backendFailure.IsCompleted) throw new InvalidOperationException(await backendFailure);
                var position = await PositionAsync(session, cancellation.Token);
                if (position >= endAt) break;
                if (await session.GetPropertyAsync("eof-reached", cancellation.Token) is true) { window.EndedAtEof = true; break; }
                if (clock.Elapsed.TotalSeconds > scenario.SteadySeconds + 30) throw new TimeoutException("稳态窗口内媒体位置没有按时推进。");
                if (clock.Elapsed.TotalSeconds >= nextReadback)
                {
                    // 与应用内 playback 模式相同的采样方式：等下一帧呈现前做一次 CPU 读回。用于量化采样开销。
                    window.ReadbackSamples++;
                    if (await probe.ReadbackAsync(cancellation.Token)) window.ReadbackNonBlackSamples++;
                    nextReadback += scenario.ReadbackIntervalSeconds;
                }
                await Task.Delay(100, cancellation.Token);
            }
            window.Statistics = await probe.EndWindowAsync(cancellation.Token);
            window.WallSeconds = clock.Elapsed.TotalSeconds;
            window.PositionEndSeconds = await PositionAsync(session, cancellation.Token);
            window.CountersAtEnd = await MpvCountersAsync(session, cancellation.Token);

            // 窗口结束后的功能检查：一次读回确认画面非黑；快照确认解码模式。二者都不影响窗口数据。
            // 若窗口已在 EOF 结束，mpv 不再产生新帧，读回无法完成，只能沿用窗口内的采样结果。
            report.NonBlackPixelObserved = window.EndedAtEof ? window.ReadbackNonBlackSamples > 0 : await probe.ReadbackAsync(cancellation.Token);
            InfoPanelSnapshot info;
            var infoWait = Stopwatch.StartNew();
            do
            {
                info = await backend.GetInfoSnapshotAsync(cancellation.Token);
                if (info.DecodeDiagnostics?.Mode is not (null or VideoDecodeMode.Unknown) || infoWait.Elapsed > TimeSpan.FromSeconds(5)) break;
                await Task.Delay(20, cancellation.Token);
            } while (true);
            report.Info = info;
            await session.CommandAsync(["stop"], cancellation.Token);
            while (await session.GetPropertyAsync("idle-active", cancellation.Token) is not true)
                await Task.Delay(20, cancellation.Token);
            report.Status = scenario.MinimumPresentedFps is { } minimum
                ? window.Statistics.PresentedFramesPerSecond >= minimum ? "通过" : "低于阈值"
                : "已测量，未判定";
        }
        catch (Exception error)
        {
            report.Status = "失败";
            report.Error = error.Message;
            throw;
        }
        finally
        {
            report.ElapsedSeconds = (DateTimeOffset.UtcNow - report.CapturedAt).TotalSeconds;
            report.CpuSeconds = (process.TotalProcessorTime - initialCpu).TotalSeconds;
            report.WorkingSetBytes = process.WorkingSet64;
            var serializer = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, serializer));
            trace.Flush();
            Trace.Listeners.Remove(trace);
            cancellation.Cancel();
            try { await backendFailure; }
            catch (OperationCanceledException) { }
        }
        return report;
    }

    private static async Task WaitForPositionAsync(MpvPlayerSession session, Task<string> failure, double seconds, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (await PositionAsync(session, cancellationToken) < seconds)
        {
            if (failure.IsCompleted) throw new InvalidOperationException(await failure);
            if (clock.Elapsed > TimeSpan.FromSeconds(seconds + 30)) throw new TimeoutException($"预热阶段未在限时内到达 {seconds} 秒。");
            await Task.Delay(50, cancellationToken);
        }
    }

    private static async Task<double> PositionAsync(MpvPlayerSession session, CancellationToken cancellationToken) =>
        await session.GetPropertyAsync("time-pos", cancellationToken) is double position ? position : 0;

    private static async Task<MpvCounters> MpvCountersAsync(MpvPlayerSession session, CancellationToken cancellationToken) => new()
    {
        VoDroppedFrames = await CounterAsync(session, "frame-drop-count", cancellationToken),
        DecoderDroppedFrames = await CounterAsync(session, "decoder-frame-drop-count", cancellationToken),
        VoDelayedFrames = await CounterAsync(session, "vo-delayed-frame-count", cancellationToken),
        MistimedFrames = await CounterAsync(session, "mistimed-frame-count", cancellationToken),
        EstimatedVfFps = await NumberAsync(session, "estimated-vf-fps", cancellationToken),
        ContainerFps = await NumberAsync(session, "container-fps", cancellationToken),
        HwdecCurrent = await session.GetPropertyAsync("hwdec-current", cancellationToken) as string,
    };

    private static async Task<long?> CounterAsync(MpvPlayerSession session, string property, CancellationToken cancellationToken)
    {
        try { return await session.GetPropertyAsync(property, cancellationToken) switch { long value => value, int value => value, _ => null }; }
        catch (MpvException) { return null; }
    }

    private static async Task<double?> NumberAsync(MpvPlayerSession session, string property, CancellationToken cancellationToken)
    {
        try { return await session.GetPropertyAsync(property, cancellationToken) switch { double value => value, long value => value, _ => null }; }
        catch (MpvException) { return null; }
    }

    private static async Task<string> ObserveFailureAsync(LibMpvBackend backend, CancellationToken cancellationToken)
    {
        await foreach (var change in backend.ObserveEventsAsync(cancellationToken))
            if (change is BackendFaulted failure) return failure.Message;
        return "播放器会话在性能测量完成前关闭。";
    }

    public sealed class PerformanceScenario
    {
        public required string MediaPath { get; init; }
        public required string Label { get; init; }
        /// <summary>sdr：BGRA8 + sRGB 色调映射；hdr：R10G10B10A2 + PQ 输出（不设 DXGI 色彩空间，离屏无显示器）。</summary>
        public string Output { get; init; } = "sdr";
        public double PeakLuminance { get; init; } = 1000;
        public uint SurfaceWidth { get; init; } = 3840;
        public uint SurfaceHeight { get; init; } = 2160;
        public double WarmupSeconds { get; init; } = 3;
        public double SteadySeconds { get; init; } = 20;
        /// <summary>d3d11va（产品配置）或 no（软解对照）。</summary>
        public string Hwdec { get; init; } = "d3d11va";
        public bool RequireHardwareDecode { get; init; } = true;
        /// <summary>1 为产品路径；0 用于观察不受 vsync 节流的原始吞吐。</summary>
        public uint SyncInterval { get; init; } = 1;
        /// <summary>0 表示窗口内不做任何 CPU 读回；大于 0 时按该秒数间隔做读回，用于量化采样开销。</summary>
        public double ReadbackIntervalSeconds { get; init; }
        public double? MinimumPresentedFps { get; init; }
        /// <summary>场景声明的目标帧率，仅记录；未声明时由报告读者按 container-fps 解读。</summary>
        public double? TargetFps { get; init; }
        public string BuildConfiguration { get; } =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        public static PerformanceScenario FromEnvironment()
        {
            var media = Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_MEDIA")!;
            var output = (Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_OUTPUT") ?? "sdr").Trim().ToLowerInvariant();
            output.Should().BeOneOf(new[] { "sdr", "hdr" }, "MPVSHELL_TEST_PERF_OUTPUT 只能是 sdr 或 hdr");
            var hwdec = (Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_HWDEC") ?? "d3d11va").Trim().ToLowerInvariant();
            hwdec.Should().BeOneOf(new[] { "d3d11va", "no" }, "MPVSHELL_TEST_PERF_HWDEC 只能是 d3d11va 或 no");
            var surface = (Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_SURFACE") ?? "3840x2160").Split('x', 2);
            surface.Should().HaveCount(2, "MPVSHELL_TEST_PERF_SURFACE 形如 3840x2160");
            var syncInterval = uint.Parse(Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_SYNC_INTERVAL") ?? "1", CultureInfo.InvariantCulture);
            syncInterval.Should().BeInRange(0u, 4u);
            var label = Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_LABEL");
            if (string.IsNullOrWhiteSpace(label))
                label = $"{Path.GetFileNameWithoutExtension(media)}-{output}-{hwdec}-vsync{syncInterval}";
            return new PerformanceScenario
            {
                MediaPath = media,
                Label = string.Concat(label.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)),
                Output = output,
                PeakLuminance = Number("MPVSHELL_TEST_PERF_PEAK_NITS") ?? 1000,
                SurfaceWidth = uint.Parse(surface[0], CultureInfo.InvariantCulture),
                SurfaceHeight = uint.Parse(surface[1], CultureInfo.InvariantCulture),
                WarmupSeconds = Number("MPVSHELL_TEST_PERF_WARMUP_SECONDS") ?? 3,
                SteadySeconds = Number("MPVSHELL_TEST_PERF_SECONDS") ?? 20,
                Hwdec = hwdec,
                RequireHardwareDecode = (Environment.GetEnvironmentVariable("MPVSHELL_TEST_PERF_REQUIRE_HWDEC") ?? "1").Trim() is not ("0" or "no" or "false"),
                SyncInterval = syncInterval,
                ReadbackIntervalSeconds = Number("MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS") ?? 0,
                MinimumPresentedFps = Number("MPVSHELL_TEST_PERF_MIN_FPS"),
                TargetFps = Number("MPVSHELL_TEST_PERF_TARGET_FPS"),
            };
        }

        private static double? Number(string variable)
        {
            var text = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(text) ? null : double.Parse(text, CultureInfo.InvariantCulture);
        }
    }

    public sealed class PerformanceReport
    {
        public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;
        public string OperatingSystem { get; } = Environment.OSVersion.VersionString;
        public required PerformanceScenario Scenario { get; init; }
        public required string MediaFile { get; init; }
        public string? MediaSha256 { get; set; }
        public string? MpvSha256 { get; set; }
        public RenderDeviceInfo? Device { get; set; }
        public double MediaDurationSeconds { get; set; }
        public SteadyWindow Steady { get; } = new();
        public bool NonBlackPixelObserved { get; set; }
        public InfoPanelSnapshot? Info { get; set; }
        public string Status { get; set; } = "未完成";
        public string? Error { get; set; }
        public double ElapsedSeconds { get; set; }
        public double CpuSeconds { get; set; }
        public long WorkingSetBytes { get; set; }
        public string? ReportPath { get; set; }
    }

    public sealed class SteadyWindow
    {
        public double PositionStartSeconds { get; set; }
        public double PositionEndSeconds { get; set; }
        public double PositionAdvancedSeconds => PositionEndSeconds - PositionStartSeconds;
        public double WallSeconds { get; set; }
        public bool EndedAtEof { get; set; }
        public int ReadbackSamples { get; set; }
        public int ReadbackNonBlackSamples { get; set; }
        public RenderStatisticsSnapshot Statistics { get; set; } = new(0, 0, 0, 0, FrameTimingSummary.Empty, FrameTimingSummary.Empty, FrameTimingSummary.Empty);
        public MpvCounters? CountersAtStart { get; set; }
        public MpvCounters? CountersAtEnd { get; set; }
        public long? VoDroppedFrames => CountersAtEnd?.VoDroppedFrames - CountersAtStart?.VoDroppedFrames;
        public long? DecoderDroppedFrames => CountersAtEnd?.DecoderDroppedFrames - CountersAtStart?.DecoderDroppedFrames;
        public long? VoDelayedFrames => CountersAtEnd?.VoDelayedFrames - CountersAtStart?.VoDelayedFrames;
        public long? MistimedFrames => CountersAtEnd?.MistimedFrames - CountersAtStart?.MistimedFrames;
    }

    public sealed class MpvCounters
    {
        public long? VoDroppedFrames { get; init; }
        public long? DecoderDroppedFrames { get; init; }
        public long? VoDelayedFrames { get; init; }
        public long? MistimedFrames { get; init; }
        public double? EstimatedVfFps { get; init; }
        public double? ContainerFps { get; init; }
        public string? HwdecCurrent { get; init; }
    }

    /// <summary>离屏渲染探针：与产品渲染器相同的 Render API → ANGLE → Composition SwapChain 链路，但不绑定面板。</summary>
    private sealed class PerformanceProbe : IAsyncDisposable
    {
        private readonly MpvPlayerSession _session;
        private readonly PerformanceScenario _scenario;
        private readonly RenderWorker _worker;
        private readonly RenderStatisticsCollector _collector = new();
        private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private D3D11DeviceManager? _device;
        private CompositionSwapChain? _swapChain;
        private AngleContext? _angle;
        private MpvRenderContext? _render;
        private TaskCompletionSource<bool>? _readback;
        private long _wakeTimestamp;
        private bool _measuring;

        public PerformanceProbe(MpvPlayerSession session, PerformanceScenario scenario)
        {
            _session = session;
            _scenario = scenario;
            _worker = new RenderWorker(Render, error => _failed.TrySetException(error));
        }

        public RenderDeviceInfo? DeviceInfo { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _device = new D3D11DeviceManager();
            _device.Initialize();
            _angle = new AngleContext(_device.GetDevicePointer());
            var format = _scenario.Output == "hdr" ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm;
            _swapChain = new CompositionSwapChain(_device.GetFactory(), _device.GetDevice(), _scenario.SurfaceWidth, _scenario.SurfaceHeight, format);
            _angle.ImportBackBuffer(_swapChain.GetBackBuffer());
            _render = _session.CreateRenderContext(_angle.GetProcAddress, () =>
            {
                Interlocked.CompareExchange(ref _wakeTimestamp, Stopwatch.GetTimestamp(), 0);
                _worker.Wake();
            });
            DeviceInfo = new RenderDeviceInfo(_device.AdapterDescription, _device.IsDebugLayerEnabled, _device.IsWarpDevice,
                _angle.Description, format.ToString(), _scenario.SurfaceWidth, _scenario.SurfaceHeight);
        }, cancellationToken);

        public Task BeginWindowAsync(CancellationToken cancellationToken) => _worker.InvokeAsync(() =>
        {
            _collector.Reset();
            _measuring = true;
        }, cancellationToken);

        public async Task<RenderStatisticsSnapshot> EndWindowAsync(CancellationToken cancellationToken)
        {
            RenderStatisticsSnapshot? snapshot = null;
            await _worker.InvokeAsync(() =>
            {
                _measuring = false;
                snapshot = _collector.Snapshot(reset: false);
            }, cancellationToken);
            return snapshot!;
        }

        /// <summary>请求在下一次呈现前做一次 CPU 读回，返回是否观察到非黑像素。呈现节奏由 mpv 决定，这里只等待。</summary>
        public async Task<bool> ReadbackAsync(CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _readback, completion, null) is not null)
                throw new InvalidOperationException("已存在未完成的读回请求。");
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        private void Render()
        {
            if (_render is null || _failed.Task.IsFaulted) return;
            _angle!.MakeBackBufferCurrent();
            if (!_render.Update())
            {
                Interlocked.Exchange(ref _wakeTimestamp, 0);
                return;
            }
            var wake = Interlocked.Exchange(ref _wakeTimestamp, 0);
            var renderStart = Stopwatch.GetTimestamp();
            _render.Render(0, checked((int)_scenario.SurfaceWidth), checked((int)_scenario.SurfaceHeight), false, _angle.GlInternalFormat, _angle.ColorDepth);
            _angle.PreparePresent();
            var readback = Interlocked.Exchange(ref _readback, null);
            if (readback is not null)
            {
                try { readback.TrySetResult(HasNonBlackPixel()); }
                catch (Exception ex) { readback.TrySetException(ex); }
            }
            var presentStart = Stopwatch.GetTimestamp();
            _swapChain!.Present(_scenario.SyncInterval);
            var presentEnd = Stopwatch.GetTimestamp();
            _render.ReportSwap();
            if (!_measuring) return;
            _collector.RecordPresented(
                Stopwatch.GetElapsedTime(renderStart, presentStart).TotalMilliseconds,
                Stopwatch.GetElapsedTime(presentStart, presentEnd).TotalMilliseconds,
                wake > 0 ? Stopwatch.GetElapsedTime(wake, presentEnd).TotalMilliseconds : null);
        }

        private bool HasNonBlackPixel()
        {
            using var backBuffer = _swapChain!.GetBackBuffer();
            var description = backBuffer.Description;
            var format = description.Format;
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
                for (var row = 1; row < 16; row++)
                for (var column = 1; column < 16; column++)
                {
                    var pointer = mapped.DataPointer + checked((int)(row * description.Height / 16 * mapped.RowPitch + column * description.Width / 16 * 4));
                    if (format == Format.R10G10B10A2_UNorm)
                    {
                        var pixel = unchecked((uint)Marshal.ReadInt32(pointer));
                        if ((pixel & 1023) + ((pixel >> 10) & 1023) + ((pixel >> 20) & 1023) > 80) return true;
                    }
                    else if (Marshal.ReadByte(pointer) + Marshal.ReadByte(pointer, 1) + Marshal.ReadByte(pointer, 2) > 20) return true;
                }
                return false;
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
