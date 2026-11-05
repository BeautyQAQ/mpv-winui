#if DEBUG
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MpvShell.App.ViewModels;
using MpvShell.Player.LibMpv;
using MpvShell.Rendering.WinUI;

namespace MpvShell.App.Diagnostics;

internal sealed partial class AppRecoveryProbe
{
    internal sealed class ContinuousPlaybackPlan
    {
        public ContinuousPlaybackPlan() { }

        public string[] MediaPaths { get; init; } = [];
        public int Cycles { get; init; } = 2;
        public double PlaySeconds { get; init; } = 1.5;
        public double DurationSeconds { get; init; }
    }

    // 解析只做配置和路径形式校验，不访问文件或创建播放器，便于先验证有界参数。
    internal static ContinuousPlaybackPlan ParseContinuousPlan(string json)
    {
        var plan = JsonSerializer.Deserialize<ContinuousPlaybackPlan>(json,
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("连续测试计划不能为空。");
        if (plan.MediaPaths is null || plan.MediaPaths.Length < 2)
            throw new ArgumentException("连续测试至少需要两个不同的本地媒体路径。");
        var paths = plan.MediaPaths.Select(path =>
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("连续测试媒体必须使用本地文件的完整路径。");
            return Path.GetFullPath(path);
        }).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("连续测试媒体路径不能重复。");
        if (plan.Cycles is < 1 or > 10000 || !double.IsFinite(plan.PlaySeconds) || plan.PlaySeconds is < 0.5 or > 30 ||
            !double.IsFinite(plan.DurationSeconds) || plan.DurationSeconds is < 0 or > 86400)
            throw new ArgumentException("连续测试轮数须为 1～10000，播放秒数须为 0.5～30，持续秒数须为 0～86400，且不能使用非有限值。");
        return new ContinuousPlaybackPlan
        {
            MediaPaths = paths, Cycles = plan.Cycles, PlaySeconds = plan.PlaySeconds, DurationSeconds = plan.DurationSeconds,
        };
    }

    private static ContinuousPlaybackPlan ReadContinuousPlan(string[] arguments)
    {
        var path = GetArgument(arguments, "--recovery-test-continuous-plan=");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("continuous 需要完整路径的 --recovery-test-continuous-plan。");
        var plan = ParseContinuousPlan(File.ReadAllText(path));
        foreach (var mediaPath in plan.MediaPaths)
            if (!File.Exists(mediaPath)) throw new FileNotFoundException("连续测试媒体不存在。", mediaPath);
        return plan;
    }

    private async Task VerifyContinuousPlaybackAsync(D3D11VideoSurfaceRenderer renderer, MpvPlayerSession session,
        PlayerViewModel vm, Dictionary<string, object?> report, ContinuousPlaybackPlan plan)
    {
        var processId = Environment.ProcessId;
        var sessionId = RuntimeHelpers.GetHashCode(session);
        var steps = new List<Dictionary<string, object?>>();
        report["ProcessId"] = processId;
        report["SessionInstanceId"] = sessionId;
        report["ViewModelInstanceId"] = RuntimeHelpers.GetHashCode(vm);
        report["RendererInstanceId"] = RuntimeHelpers.GetHashCode(renderer);
        report["ContinuousPlan"] = plan;
        report["ContinuousSteps"] = steps;
        report["ContinuousCyclesCompleted"] = 0;
        report["ContinuousCompleted"] = false;
        var clock = Stopwatch.StartNew();
        var cycle = 0;
        do
        {
            cycle++;
            for (var mediaIndex = 0; mediaIndex < plan.MediaPaths.Length; mediaIndex++)
            {
                var path = plan.MediaPaths[mediaIndex];
                var actions = new List<Dictionary<string, object?>>();
                var step = new Dictionary<string, object?>
                {
                    ["Cycle"] = cycle, ["MediaIndex"] = mediaIndex + 1, ["MediaPath"] = path,
                    ["ProcessId"] = processId, ["SessionInstanceId"] = sessionId,
                    ["StartedAtUtc"] = DateTimeOffset.UtcNow, ["Completed"] = false, ["Actions"] = actions,
                };
                steps.Add(step);
                Require(Environment.ProcessId == processId && RuntimeHelpers.GetHashCode(session) == sessionId &&
                    ReferenceEquals(_latestRenderer, renderer), "连续测试必须保持同一应用进程、会话和渲染器。");
                await ContinuousActionAsync("打开媒体", cycle, mediaIndex, actions, 30, async (token, action) =>
                {
                    EnsureContinuousReady(vm);
                    vm.UrlText = path;
                    Require(vm.OpenUrlCommand.CanExecute(null), "打开媒体命令必须可执行。");
                    _pendingLoad = vm.OpenUrlCommand.ExecuteAsync(null);
                    await _pendingLoad.WaitAsync(token);
                    await WaitContinuousStateAsync(() => !vm.IsLoading && !vm.IsStopping && vm.State.IsPlaying &&
                        !vm.State.IsStopped && ContinuousPathEquals(vm.State.CurrentUrl, path), vm, token);
                    await VerifyContinuousIdentityAsync(session, vm, path, token);
                    action["NativePath"] = await session.GetPropertyAsync("path", token);
                });
                await ContinuousActionAsync("播放推进与非黑画面", cycle, mediaIndex, actions, plan.PlaySeconds + 20,
                    (token, action) => VerifyContinuousProgressAsync(renderer, session, vm, path, plan.PlaySeconds, token, action));
                await ContinuousActionAsync("暂停并验证稳定", cycle, mediaIndex, actions, 20, async (token, action) =>
                {
                    await VerifyContinuousIdentityAsync(session, vm, path, token);
                    Require(vm.State.IsPlaying && vm.TogglePlayPauseCommand.CanExecute(null), "暂停前媒体必须正在播放。");
                    await vm.TogglePlayPauseCommand.ExecuteAsync(null).WaitAsync(token);
                    await WaitContinuousStateAsync(() => !vm.State.IsPlaying && !vm.State.IsStopped, vm, token);
                    double? previous = null;
                    double pausedAt;
                    while (true)
                    {
                        await VerifyContinuousIdentityAsync(session, vm, path, token);
                        Require(await session.GetPropertyAsync("pause", token) is true, "暂停必须到达原生会话。");
                        var seeking = await session.GetPropertyAsync("seeking", token) is true;
                        pausedAt = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
                        if (!seeking && previous is { } last && Math.Abs(pausedAt - last) < 0.001) break;
                        previous = seeking ? null : pausedAt;
                        await Task.Delay(100, token);
                    }
                    var before = await CaptureAsync(renderer).WaitAsync(token);
                    await Task.Delay(500, token);
                    await VerifyContinuousIdentityAsync(session, vm, path, token);
                    var after = await CaptureAsync(renderer).WaitAsync(token);
                    var pausedEnd = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
                    Require(!vm.State.IsPlaying && await session.GetPropertyAsync("pause", token) is true &&
                        Math.Abs(pausedEnd - pausedAt) < 0.001, "暂停期间媒体时钟必须保持稳定。");
                    Require(before.HasColor && after.HasColor && before.Difference(after) < 2.0 / 255,
                        "暂停期间必须保持同一非黑画面。");
                    action["PausedPosition"] = pausedAt;
                    action["PausedEndPosition"] = pausedEnd;
                    action["PixelDifference"] = before.Difference(after);
                });
                await ContinuousActionAsync("恢复播放", cycle, mediaIndex, actions, plan.PlaySeconds + 20, async (token, action) =>
                {
                    await VerifyContinuousIdentityAsync(session, vm, path, token);
                    Require(!vm.State.IsPlaying && !vm.State.IsStopped && vm.TogglePlayPauseCommand.CanExecute(null),
                        "恢复前媒体必须处于已加载的暂停状态。");
                    await vm.TogglePlayPauseCommand.ExecuteAsync(null).WaitAsync(token);
                    await WaitContinuousStateAsync(() => vm.State.IsPlaying && !vm.State.IsStopped, vm, token);
                    await VerifyContinuousProgressAsync(renderer, session, vm, path, plan.PlaySeconds, token, action);
                });
                await ContinuousActionAsync("停止并清零", cycle, mediaIndex, actions, 20, async (token, action) =>
                {
                    await VerifyContinuousIdentityAsync(session, vm, path, token);
                    Require(vm.StopCommand.CanExecute(null), "停止命令必须可执行。");
                    await vm.StopCommand.ExecuteAsync(null).WaitAsync(token);
                    await WaitContinuousStateAsync(() => vm.State.IsStopped && !vm.State.IsPlaying && !vm.IsLoading &&
                        !vm.IsStopping && vm.State.PositionSeconds == 0 && vm.State.DurationSeconds == 0, vm, token);
                    EnsureContinuousReady(vm, path);
                    Require(!vm.IsBuffering && await session.GetPropertyAsync("idle-active", token) is true,
                        "停止必须卸载原生媒体并清除缓冲状态。");
                    var nativePath = await OptionalStringAsync(session, "path").WaitAsync(token);
                    Require(nativePath is null, "停止后原生媒体路径必须已卸载。");
                    action["NativePath"] = nativePath;
                    action["IsStopped"] = vm.State.IsStopped;
                    action["PositionSeconds"] = vm.State.PositionSeconds;
                    action["DurationSeconds"] = vm.State.DurationSeconds;
                });
                await ContinuousActionAsync("停止后重播", cycle, mediaIndex, actions, plan.PlaySeconds + 30, async (token, action) =>
                {
                    EnsureContinuousReady(vm, path);
                    Require(vm.State.IsStopped && vm.TogglePlayPauseCommand.CanExecute(null), "重播必须从停止状态提交播放命令。");
                    _pendingLoad = vm.TogglePlayPauseCommand.ExecuteAsync(null);
                    await _pendingLoad.WaitAsync(token);
                    await WaitContinuousStateAsync(() => !vm.IsLoading && !vm.IsStopping && vm.State.IsPlaying &&
                        !vm.State.IsStopped, vm, token);
                    await VerifyContinuousProgressAsync(renderer, session, vm, path, plan.PlaySeconds, token, action);
                });
                step["Completed"] = true;
                step["CompletedAtUtc"] = DateTimeOffset.UtcNow;
                Stage($"连续测试第 {cycle} 轮媒体 {mediaIndex + 1} 完成，同一进程内打开、暂停恢复、停止重播均通过");
            }
            report["ContinuousCyclesCompleted"] = cycle;
            report["ContinuousElapsedSeconds"] = clock.Elapsed.TotalSeconds;
        } while (plan.DurationSeconds > 0 ? clock.Elapsed.TotalSeconds < plan.DurationSeconds : cycle < plan.Cycles);
        report["ContinuousCompleted"] = true;
        Stage($"连续测试已完成 {cycle} 个完整轮次；本记录仅描述实际执行时长与次数");
    }

    private async Task ContinuousActionAsync(string name, int cycle, int mediaIndex, List<Dictionary<string, object?>> actions,
        double timeoutSeconds, Func<CancellationToken, Dictionary<string, object?>, Task> operation)
    {
        var action = new Dictionary<string, object?>
        {
            ["Name"] = name, ["StartedAtUtc"] = DateTimeOffset.UtcNow, ["Status"] = "Running", ["TimeoutSeconds"] = timeoutSeconds,
        };
        actions.Add(action);
        Stage($"连续测试第 {cycle} 轮媒体 {mediaIndex + 1} 开始：{name}");
        var clock = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        try
        {
            await operation(cancellation.Token, action).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            action["Status"] = "Passed";
        }
        catch (Exception error)
        {
            action["Status"] = "Failed";
            action["Error"] = error.ToString();
            throw;
        }
        finally
        {
            cancellation.Cancel();
            action["ElapsedSeconds"] = clock.Elapsed.TotalSeconds;
            Stage($"连续测试第 {cycle} 轮媒体 {mediaIndex + 1} {name}：{action["Status"]}，耗时 {clock.Elapsed.TotalSeconds:0.000} 秒");
        }
    }

    private async Task VerifyContinuousProgressAsync(D3D11VideoSurfaceRenderer renderer, MpvPlayerSession session,
        PlayerViewModel vm, string path, double playSeconds, CancellationToken token, Dictionary<string, object?> action)
    {
        await VerifyContinuousIdentityAsync(session, vm, path, token);
        var positionBefore = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
        var framesBefore = Interlocked.Read(ref _frames);
        double positionAfter;
        do
        {
            token.ThrowIfCancellationRequested();
            EnsureContinuousReady(vm, path);
            Require(vm.State.IsPlaying && !vm.State.IsStopped, "连续播放不得在推进检查完成前停止。");
            positionAfter = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
            if (positionAfter - positionBefore >= playSeconds && Interlocked.Read(ref _frames) > framesBefore) break;
            await Task.Delay(50, token);
        } while (true);
        FrameSample frame;
        do
        {
            EnsureContinuousReady(vm, path);
            Require(vm.State.IsPlaying && !vm.State.IsStopped, "等待非黑画面时媒体必须继续播放。");
            frame = await CaptureAsync(renderer, forceRedraw: false).WaitAsync(token);
        } while (!frame.HasColor);
        await VerifyContinuousIdentityAsync(session, vm, path, token);
        action["PositionBefore"] = positionBefore;
        action["PositionAfter"] = positionAfter;
        action["FramesBefore"] = framesBefore;
        action["FramesAfter"] = Interlocked.Read(ref _frames);
        action["Frame"] = frame.Summary;
        action["NativePath"] = await session.GetPropertyAsync("path", token);
    }

    private static void EnsureContinuousReady(PlayerViewModel vm, string? path = null)
    {
        Require(vm.ErrorMessage is null && !vm.HasTerminalFailure, vm.ErrorMessage ?? "连续测试出现不可恢复错误。");
        Require(!vm.IsLoading && !vm.IsStopping, "连续测试步骤间不能遗留加载或停止操作。");
        if (path is not null) Require(ContinuousPathEquals(vm.State.CurrentUrl, path), "页面媒体身份必须匹配当前连续测试步骤。");
    }

    private static async Task VerifyContinuousIdentityAsync(MpvPlayerSession session, PlayerViewModel vm, string path, CancellationToken token)
    {
        EnsureContinuousReady(vm, path);
        Require(ContinuousPathEquals(await session.GetPropertyAsync("path", token) as string, path),
            "原生会话媒体身份必须匹配当前连续测试步骤。");
    }

    private static async Task WaitContinuousStateAsync(Func<bool> condition, PlayerViewModel vm, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Require(vm.ErrorMessage is null && !vm.HasTerminalFailure, vm.ErrorMessage ?? "连续测试出现不可恢复错误。");
            if (condition()) return;
            await Task.Delay(25, token);
        }
    }

    private static bool ContinuousPathEquals(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) && Path.IsPathFullyQualified(actual) &&
        string.Equals(Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase);
}
#endif
