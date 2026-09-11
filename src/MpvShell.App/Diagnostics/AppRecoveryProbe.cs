#if DEBUG
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using MpvShell.App.ViewModels;
using MpvShell.Player.LibMpv;
using MpvShell.Rendering.WinUI;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;
using Windows.Graphics;

namespace MpvShell.App.Diagnostics;

/// <summary>由验收脚本显式启用，在真实 PlayerPage 内运行。所有 GPU 读回均限于 Debug 测试。</summary>
internal sealed class AppRecoveryProbe
{
    private readonly List<string> _stages = [];
    private TaskCompletionSource<FrameSample>? _sample;
    private (TaskCompletionSource<FrameSample> Completion, FrameSample Frame)? _pendingPresentation;
    private long _frames;
    private int _faultsRemaining;
    private int _faultsThrown;
    private int _resizeFaultPending;
    private readonly ConcurrentQueue<int> _faultGenerations = new();

    internal static async Task RunIfRequestedAsync(PlayerViewModel vm, D3D11VideoSurfaceRenderer renderer,
        IMpvPlayerSession playerSession, Func<Task> shutdown, Window window, bool ready,
        Func<bool> playbackControlsEnabled)
    {
        var arguments = Environment.GetCommandLineArgs();
        var reportPath = GetArgument(arguments, "--recovery-test-report=");
        if (reportPath is null) return;
        var mode = GetArgument(arguments, "--recovery-test-mode=") ?? "playing";
        var probe = new AppRecoveryProbe();
        var report = new Dictionary<string, object?> { ["Status"] = "Failed", ["Mode"] = mode,
            ["StartedAtUtc"] = DateTimeOffset.UtcNow, ["Stages"] = probe._stages };
        try
        {
            if (!Path.IsPathFullyQualified(reportPath)) throw new ArgumentException("验收报告必须使用绝对路径。");
            if (mode is not ("playing" or "paused" or "immediate-failure" or "rebuild-failure" or "resize" or "playback")) throw new ArgumentException("未知验收模式。");
            if (!ready) throw new InvalidOperationException(vm.ErrorMessage ?? "播放器初始化未完成。");
            if (string.IsNullOrEmpty(vm.State.CurrentUrl)) throw new InvalidOperationException("验收需要指定媒体。");
            var session = (MpvPlayerSession)playerSession;
            await session.SetPropertyAsync("mute", true, CancellationToken.None);
            await renderer.SetTestCallbacksAsync(probe.BeforeRender, probe.ReadFrame,
                probe.OnPresented, () =>
                {
                    if (mode == "rebuild-failure") throw new InvalidOperationException("验收注入：图形设备创建失败。");
                }, generation =>
                {
                    if (Interlocked.Exchange(ref probe._resizeFaultPending, 0) == 1)
                        probe.ThrowInjectedFailure(generation);
                });
            await probe.UntilAsync(() => Interlocked.Read(ref probe._frames) >= 30 && vm.State.PositionSeconds > 0,
                vm, "等待初始播放帧");
            var initial = await probe.WaitForImageAsync(renderer, vm, 1, forceRedraw: mode != "playback");
            report["Media"] = vm.State.CurrentUrl;
            report["InitialVideo"] = vm.InfoPanel.VideoSummary;
            report["InitialDecode"] = vm.InfoPanel.DecodeSummary;
            report["InitialOutput"] = vm.InfoPanel.OutputSummary;
            report["InitialFrame"] = initial.Summary;
            probe.Stage("已验证真实页面上的初始播放与非黑 GPU 像素");

            if (mode == "playback")
            {
                await probe.VerifyPlaybackToEndAsync(renderer, session, vm, report, initial);
                report["Status"] = "Passed";
                return;
            }

            if (mode == "paused")
            {
                await vm.TogglePlayPauseCommand.ExecuteAsync(null);
                await probe.UntilAsync(() => !vm.State.IsPlaying, vm, "等待暂停");
                // 在片中比较实际暂停画面，避免黑场/淡入让绝对像素误差阈值失去意义。
                var target = Math.Min(30, vm.State.DurationSeconds / 2);
                await vm.SeekToAsync(target);
                var positioned = await WaitForPausedPositionAsync(session);
                report["SeekTarget"] = target;
                report["SeekPosition"] = positioned;
                // TS 的首次 demux seek 可能落在请求点之后；这里只选择片中画面。
                // 恢复比较使用实际稳定位置，下面仍严格要求恢复到同一帧。
                Require(Math.Abs(positioned - target) < 1, "暂停定位应落在选定的片中范围。");
                initial = await probe.CaptureAsync(renderer);
                Require(initial.HasColor, "暂停前必须有有效画面。");
                report["PausedBaselineFrame"] = initial.Summary;
            }
            var pathBefore = await session.GetPropertyAsync("path", CancellationToken.None);
            var videoTrackBefore = await session.GetPropertyAsync("vid", CancellationToken.None);
            var positionBefore = vm.State.PositionSeconds;
            var nativePositionBefore = Convert.ToDouble(await session.GetPropertyAsync("time-pos", CancellationToken.None));
            report["PositionBefore"] = positionBefore;
            report["NativePositionBefore"] = nativePositionBefore;
            report["PausedBefore"] = !vm.State.IsPlaying;

            if (mode == "resize")
            {
                Interlocked.Exchange(ref probe._resizeFaultPending, 1);
                var size = window.AppWindow.Size;
                // 由真实 WinUI SizeChanged -> PlayerPage.ResizeVideoAsync 触发恢复。
                window.AppWindow.Resize(new SizeInt32(size.Width + 32, size.Height + 24));
            }
            else
            {
                Interlocked.Exchange(ref probe._faultsRemaining, mode == "immediate-failure" ? 2 : 1);
                await renderer.RequestTestFrameAsync();
            }
            if (mode is "playing" or "paused" or "resize")
            {
                var restored = await probe.WaitForImageAsync(renderer, vm, initial.Generation + 1);

                Require(Equals(pathBefore, await session.GetPropertyAsync("path", CancellationToken.None)), "恢复应保留当前媒体。");
                Require(Equals(videoTrackBefore, await session.GetPropertyAsync("vid", CancellationToken.None)), "恢复应重新选中原视频轨。");
                if (mode == "paused")
                {
                    report["FirstRestoredFrame"] = restored.Summary;
                    // seek 的命令回复早于解码完成；等待最终定位画面，再比较同一暂停帧。
                    var nativePositionAfter = await WaitForPausedPositionAsync(session);
                    report["NativePositionAfter"] = nativePositionAfter;
                    restored = await probe.CaptureAsync(renderer);
                    Require(!vm.State.IsPlaying, "恢复不能解除用户暂停。");
                    Require(await session.GetPropertyAsync("pause", CancellationToken.None) is true, "mpv 应仍处于暂停状态。");
                    Require(Math.Abs(vm.State.PositionSeconds - positionBefore) < 0.15, "恢复不能跳走暂停位置。");
                    Require(Math.Abs(nativePositionAfter - nativePositionBefore) < 0.001, "原生暂停位置不能在重建中改变。");
                    var difference = initial.Difference(restored);
                    report["PausedPixelDifference"] = difference;
                    var relativeDifference = difference / Math.Max(initial.Colors.Average(), restored.Colors.Average());
                    report["PausedRelativePixelDifference"] = relativeDifference;
                    Require(difference < 2.0 / 255 && relativeDifference < 0.02, "恢复后应保留同一暂停画面。");
                }
                else
                {
                    var resumedFrames = Interlocked.Read(ref probe._frames);
                    await probe.UntilAsync(() => vm.State.PositionSeconds > positionBefore + 0.5 &&
                        Interlocked.Read(ref probe._frames) >= resumedFrames + 15, vm, "等待恢复后连续播放");
                    var laterFrame = await probe.CaptureAsync(renderer);
                    var pixelChange = restored.Difference(laterFrame);
                    report["PlayingPixelChange"] = pixelChange;
                    if (arguments.Contains("--recovery-test-animated-pattern", StringComparer.Ordinal))
                        Require(pixelChange > 0.001, "动态图案恢复后必须继续变化，不能只呈现冻结帧。");
                }
                Require(vm.ErrorMessage is null, "成功恢复后不应留下错误提示。");
                report["RestoredFrame"] = restored.Summary;
                report["PositionAfter"] = vm.State.PositionSeconds;
                report["RestoredDecode"] = vm.InfoPanel.DecodeSummary;
                report["RestoredOutput"] = vm.InfoPanel.OutputSummary;
                probe.Stage("图形链已自动重建，媒体位置、播放状态及 GPU 像素验证通过");
                Interlocked.Exchange(ref probe._faultsRemaining, 1);
                await renderer.RequestTestFrameAsync();
            }

            var expectedError = mode == "rebuild-failure" ? "自动重建未成功" : "恢复后再次失败";
            await probe.UntilAsync(() => vm.ErrorMessage?.Contains(expectedError, StringComparison.Ordinal) == true,
                vm, "等待重复故障进入页面错误提示", allowError: true);
            Require(vm.ErrorVisibility == Visibility.Visible && vm.State.AreControlsVisible, "故障必须显示错误与操作控件。");
            Require(Volatile.Read(ref probe._faultsThrown) == (mode == "rebuild-failure" ? 1 : 2), "必须实际执行指定次数的渲染线程故障注入。");
            if (mode != "rebuild-failure")
                Require(probe._faultGenerations.SequenceEqual(new[] { initial.Generation, initial.Generation + 1 }),
                    "第二次故障必须发生在重建后的图形链。");
            report["ExpectedTerminalError"] = vm.ErrorMessage;
            Require(vm.HasRenderingFailure && !vm.State.IsPlaying && !playbackControlsEnabled(),
                "不可恢复错误必须停留在终态并禁用播放入口。");
            Require(await session.GetPropertyAsync("pause", CancellationToken.None) is true,
                "不可恢复错误必须暂停原生媒体，不能留下音频继续播放。");
            // 首帧故障可能打断尚未结束的恢复 seek；等待其完成，区分一次定位更新与继续播放。
            var fatalPosition = await WaitForPausedPositionAsync(session);
            report["TerminalPositionBefore"] = fatalPosition;
            var fatalMessage = vm.ErrorMessage;
            var fatalFrames = Interlocked.Read(ref probe._frames);
            await vm.ChangeVolumeAsync(vm.State.Volume);
            await vm.TogglePlayPauseCommand.ExecuteAsync(null);
            await renderer.UpdateOutputAsync(true, new DisplayOutputCapabilities
            {
                IsAvailable = true, IsHdrSupported = true, ColorKind = DisplayOutputColorKind.HighDynamicRange,
                PeakLuminanceInNits = 1000,
            }, CancellationToken.None);
            await renderer.RequestTestFrameAsync();
            await Task.Delay(250);
            Require(Interlocked.Read(ref probe._frames) == fatalFrames, "终态后显示器变化不能重新启动呈现。");
            var terminalPause = await session.GetPropertyAsync("pause", CancellationToken.None);
            var terminalPosition = Convert.ToDouble(await session.GetPropertyAsync("time-pos", CancellationToken.None));
            report["TerminalPositionAfter"] = terminalPosition;
            report["TerminalPause"] = terminalPause;
            Require(terminalPause is true && Math.Abs(terminalPosition - fatalPosition) < 0.05,
                "终态后媒体时钟必须保持停止。");
            Require(vm.ErrorMessage == fatalMessage, "成功音量命令不能消掉未恢复的渲染错误。");
            report["TerminalStateStable"] = true;
            probe.Stage("重复故障已到达真实页面 ViewModel，并显示错误与控件");
            report["Status"] = "Passed";
        }
        catch (Exception ex) { report["Error"] = ex.ToString(); }
        finally
        {
            report["PresentedFrames"] = Interlocked.Read(ref probe._frames);
            report["InjectedFaults"] = Volatile.Read(ref probe._faultsThrown);
            report["InjectedFaultGenerations"] = probe._faultGenerations.ToArray();
            try
            {
                await shutdown().WaitAsync(TimeSpan.FromSeconds(15));
                probe.Stage("页面关闭完成，渲染线程和 mpv 会话均已释放");
                report["ShutdownCompleted"] = true;
            }
            catch (Exception ex) { report["Status"] = "Failed"; report["ShutdownError"] = ex.ToString(); }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Trace.WriteLine($"写入应用验收报告失败：{ex}"); }
            window.Close();
        }
    }

    private static string? GetArgument(string[] arguments, string prefix) =>
        arguments.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static async Task<double> WaitForPausedPositionAsync(MpvPlayerSession session)
    {
        var clock = Stopwatch.StartNew();
        double? previous = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Require(await session.GetPropertyAsync("pause", CancellationToken.None) is true,
                "等待暂停定位时原生媒体不能恢复播放。");
            var seeking = await session.GetPropertyAsync("seeking", CancellationToken.None) is true;
            var position = Convert.ToDouble(await session.GetPropertyAsync("time-pos", CancellationToken.None));
            if (!seeking && previous is { } last && Math.Abs(position - last) < 0.001) return position;
            previous = seeking ? null : position;
            await Task.Delay(250);
        }
        throw new TimeoutException("暂停后的原生定位未稳定。");
    }

    private void BeforeRender(int generation)
    {
        if (Volatile.Read(ref _faultsRemaining) <= 0) return;
        Interlocked.Decrement(ref _faultsRemaining);
        ThrowInjectedFailure(generation);
    }

    private void ThrowInjectedFailure(int generation)
    {
        Interlocked.Increment(ref _faultsThrown);
        _faultGenerations.Enqueue(generation);
        throw new COMException($"验收注入 DXGI_ERROR_DEVICE_REMOVED，图形代次 {generation}", unchecked((int)0x887A0005));
    }

    private async Task<FrameSample> CaptureAsync(D3D11VideoSurfaceRenderer renderer, bool forceRedraw = true)
    {
        var completion = new TaskCompletionSource<FrameSample>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _sample, completion, null) is not null)
            throw new InvalidOperationException("已经存在像素读回请求。");
        if (forceRedraw) await renderer.RequestTestFrameAsync();
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private async Task<FrameSample> WaitForImageAsync(D3D11VideoSurfaceRenderer renderer, PlayerViewModel vm, int generation,
        bool forceRedraw = true)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (vm.ErrorMessage is not null) throw new InvalidOperationException(vm.ErrorMessage);
            var sample = await CaptureAsync(renderer, forceRedraw);
            if (sample.Generation >= generation && sample.HasColor) return sample;
            await Task.Delay(25);
        }
        throw new TimeoutException("未得到指定图形代次的有效媒体像素。");
    }

    private async Task VerifyPlaybackToEndAsync(D3D11VideoSurfaceRenderer renderer, MpvPlayerSession session,
        PlayerViewModel vm, Dictionary<string, object?> report, FrameSample initial)
    {
        var duration = vm.State.DurationSeconds;
        Require(duration > 0, "完整播放验收需要已知时长的文件。");
        var decoder = await session.GetPropertyAsync("hwdec-current", CancellationToken.None);
        var samples = new List<object>();
        report["PlaybackSamples"] = samples;
        var previous = initial;
        var changedFrames = 0;
        var nextSampleAt = Math.Max(5, vm.State.PositionSeconds + 5);
        var clock = Stopwatch.StartNew();
        while (vm.State.IsPlaying)
        {
            if (vm.ErrorMessage is not null) throw new InvalidOperationException(vm.ErrorMessage);
            if (clock.Elapsed.TotalSeconds > duration + 20) throw new TimeoutException("完整播放未正常到达 EOF。");
            if (vm.State.PositionSeconds >= nextSampleAt && vm.State.PositionSeconds < duration - 1)
            {
                // 自然播放采样不唤醒/强制渲染；让正常 mpv 帧更新与 Present 决定节奏。
                var frame = await CaptureAsync(renderer, forceRedraw: false);
                var change = previous.Difference(frame);
                if (change > 0.001) changedFrames++;
                Require(Equals(decoder, await session.GetPropertyAsync("hwdec-current", CancellationToken.None)),
                    "完整播放期间解码模式不应悄然改变。");
                samples.Add(new { Position = vm.State.PositionSeconds, PixelChange = change, Frame = frame.Summary,
                    Decode = vm.InfoPanel.DecodeSummary });
                previous = frame;
                nextSampleAt += Math.Max(5, duration / 6);
            }
            await Task.Delay(100);
        }
        Require(vm.ErrorMessage is null && !vm.IsBuffering && vm.State.PositionSeconds >= duration - 0.5,
            "完整播放应正常结束，并清除缓冲提示。");
        Require(changedFrames >= 2, "完整播放应观察到多个不同时刻的动态图像。");
        report["EndedAt"] = vm.State.PositionSeconds;
        report["DurationSeconds"] = duration;
        report["NaturalPlaybackSeconds"] = clock.Elapsed.TotalSeconds;
        report["ChangedSampleCount"] = changedFrames;
        Stage("自然播放至 EOF，多个间隔画面有变化、解码模式保持不变且没有页面错误");
    }

    private void ReadFrame(D3D11DeviceManager device, CompositionSwapChain swapChain, int generation)
    {
        var completion = Interlocked.Exchange(ref _sample, null);
        if (completion is null) return;
        try
        {
            using var buffer = swapChain.GetBackBuffer();
            var description = buffer.Description;
            var format = description.Format;
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
                var colors = new double[16 * 16 * 3];
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var row = (uint)((y + 0.5) * description.Height / 16);
                    var column = (uint)((x + 0.5) * description.Width / 16);
                    var pointer = mapped.DataPointer + checked((int)(row * mapped.RowPitch + column * 4));
                    var index = (y * 16 + x) * 3;
                    if (format == Format.R10G10B10A2_UNorm)
                    {
                        var pixel = unchecked((uint)Marshal.ReadInt32(pointer));
                        colors[index] = (pixel & 1023) / 1023.0;
                        colors[index + 1] = ((pixel >> 10) & 1023) / 1023.0;
                        colors[index + 2] = ((pixel >> 20) & 1023) / 1023.0;
                    }
                    else if (format == Format.B8G8R8A8_UNorm)
                    {
                        colors[index] = Marshal.ReadByte(pointer, 2) / 255.0;
                        colors[index + 1] = Marshal.ReadByte(pointer, 1) / 255.0;
                        colors[index + 2] = Marshal.ReadByte(pointer) / 255.0;
                    }
                    else throw new NotSupportedException($"未支持的验收像素格式：{format}");
                }
                _pendingPresentation = (completion, new FrameSample(generation, description.Width, description.Height, format, colors));
            }
            finally { context.Unmap(staging, 0); }
        }
        catch (Exception ex) { completion.TrySetException(ex); }
    }

    private void OnPresented()
    {
        Interlocked.Increment(ref _frames);
        if (_pendingPresentation is not { } pending) return;
        _pendingPresentation = null;
        pending.Completion.TrySetResult(pending.Frame);
    }

    private async Task UntilAsync(Func<bool> condition, PlayerViewModel vm, string stage, bool allowError = false)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (!allowError && vm.ErrorMessage is not null) throw new InvalidOperationException(vm.ErrorMessage);
            if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(stage);
            await Task.Delay(25);
        }
    }

    private void Stage(string message) { _stages.Add(message); Trace.WriteLine($"[AppRecoveryProbe] {message}"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed record FrameSample(int Generation, uint Width, uint Height, Format Format, double[] Colors)
    {
        public bool HasColor => Colors.Max() > 0.08 && Colors.Average() > 0.005;
        public object Summary => new { Generation, Width, Height, Format = Format.ToString(), HasColor, Mean = Colors.Average() };
        public double Difference(FrameSample other) => Colors.Zip(other.Colors, (a, b) => Math.Abs(a - b)).Average();
    }
}
#endif
