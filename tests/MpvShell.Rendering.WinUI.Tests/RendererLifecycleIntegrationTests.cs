using System.Diagnostics;
using FluentAssertions;
using MpvShell.Player.LibMpv;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>
/// 渲染器释放/重建与共享会话的视频交接：释放前挂起、重建后恢复，媒体与位置保持，
/// 关闭不因挂起租约而悬挂。真实 libmpv/ANGLE/D3D11 集成测试，运行前提与 NativeRenderIntegrationTests 相同。
/// </summary>
public sealed class RendererLifecycleIntegrationTests
{
    [Fact]
    public async Task Disposing_a_renderer_during_playback_should_suspend_and_a_new_renderer_should_resume_playback()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-handoff-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            var renderer1 = new D3D11VideoSurfaceRenderer();
            await renderer1.InitializeAsync(session, cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, 1.0, cancellation.Token);
            var videoTrackBefore = await session.GetPropertyAsync("vid", cancellation.Token);
            videoTrackBefore.Should().NotBe("no");

            // 播放中释放渲染器：释放前必须挂起视频并保留媒体，租约由挂起状态继续持有。
            await renderer1.DisposeAsync();
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(true, "释放渲染链前必须暂停媒体");
            var detachedSelection = await session.GetPropertyAsync("vid", cancellation.Token);
            (detachedSelection is false || detachedSelection is "no").Should().BeTrue("视频轨必须在释放 render context 前停用");
            (await session.GetPropertyAsync("idle-active", cancellation.Token)).Should().Be(false);
            (await session.GetPropertyAsync("path", cancellation.Token)).Should().Be(path);
            var positionFrozen = Convert.ToDouble(await session.GetPropertyAsync("time-pos", cancellation.Token));

            // 挂起期间的播放修改排队等待租约，不会并发改写媒体状态。
            var deferred = session.SetPropertyAsync("volume", 37, cancellation.Token);
            deferred.IsCompleted.Should().BeFalse();

            // 新渲染器（等价于页面重新挂载）接管后恢复：视频轨、暂停状态与位置回归连续播放。
            var renderer2 = new D3D11VideoSurfaceRenderer();
            await renderer2.InitializeAsync(session, cancellation.Token);
            await session.ResumeVideoAfterDetachAsync(cancellation.Token);
            await deferred;
            Convert.ToDouble(await session.GetPropertyAsync("volume", cancellation.Token)).Should().Be(37);
            (await session.GetPropertyAsync("vid", cancellation.Token)).Should().Be(videoTrackBefore);
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(false);
            await WaitForPositionAsync(session, positionFrozen + 0.5, cancellation.Token);
            Convert.ToInt64(await session.GetPropertyAsync("frame-drop-count", cancellation.Token)).Should().Be(0L,
                "挂起与交接期间必须及时消费 mpv 帧，不能让它等待超时后丢弃");
            await renderer2.DisposeAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Disposing_a_renderer_on_paused_media_should_restore_the_same_position_on_resume()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-handoff-paused-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            var renderer1 = new D3D11VideoSurfaceRenderer();
            await renderer1.InitializeAsync(session, cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, 0.5, cancellation.Token);
            await backend.PauseAsync(cancellation.Token);
            var positionBefore = await WaitForPausedPositionAsync(session, cancellation.Token);

            await renderer1.DisposeAsync();
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(true);
            var detachedSelection = await session.GetPropertyAsync("vid", cancellation.Token);
            (detachedSelection is false || detachedSelection is "no").Should().BeTrue("视频轨必须在释放 render context 前停用");
            Convert.ToDouble(await session.GetPropertyAsync("time-pos", cancellation.Token))
                .Should().BeApproximately(positionBefore, 0.001, "挂起期间暂停位置不能漂移");

            var renderer2 = new D3D11VideoSurfaceRenderer();
            await renderer2.InitializeAsync(session, cancellation.Token);
            await session.ResumeVideoAfterDetachAsync(cancellation.Token);
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(true, "恢复必须保留用户暂停");
            var positionAfter = await WaitForPausedPositionAsync(session, cancellation.Token);
            positionAfter.Should().BeApproximately(positionBefore, 0.001, "重新挂载后的暂停位置必须一致");
            await renderer2.DisposeAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Disposing_the_backend_after_a_renderer_suspend_should_release_the_session_without_waiting_for_resume()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-handoff-exit-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            var renderer = new D3D11VideoSurfaceRenderer();
            await renderer.InitializeAsync(session, cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, 0.5, cancellation.Token);

            // 模拟“卸载后未重新挂载就退出”：挂起租约仍被持有，关闭不能被它拖住。
            await renderer.DisposeAsync();
            var clock = Stopwatch.StartNew();
            await backend.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "挂起的播放租约不能阻止会话销毁");
            var closed = () => session.GetPropertyAsync("pause", CancellationToken.None);
            await closed.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Disposing_a_renderer_after_terminal_failure_should_skip_suspend_and_stay_idempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-handoff-fatal-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            var renderer = new D3D11VideoSurfaceRenderer();
            await renderer.InitializeAsync(session, cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, 0.5, cancellation.Token);
            await session.PauseForRenderingFailureAsync(cancellation.Token);

            await renderer.DisposeAsync();
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(true);
            // 终态下不做视频挂起改写；释放活动视频链可能终止只有视频的媒体（EndFile），
            // 因此不断言轨道选择，只验证释放不悬挂、可重复且不遗留播放租约。
            await renderer.DisposeAsync(); // 重复释放幂等

            // 终态下租约未被挂起占用：被封锁的播放修改立即失败而不是等待。
            var resume = () => session.SetPropertyAsync("pause", false, cancellation.Token);
            await resume.Should().ThrowExactlyAsync<InvalidOperationException>();
            await backend.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { File.Delete(path); }
    }

    private static async Task WaitForPositionAsync(MpvPlayerSession session, double seconds, CancellationToken cancellationToken)
    {
        while (await session.GetPropertyAsync("time-pos", cancellationToken) is not double position || position < seconds)
            await Task.Delay(50, cancellationToken);
    }

    private static async Task<double> WaitForPausedPositionAsync(MpvPlayerSession session, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        double? previous = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var seeking = await session.GetPropertyAsync("seeking", cancellationToken) is true;
            var position = Convert.ToDouble(await session.GetPropertyAsync("time-pos", cancellationToken));
            if (!seeking && previous is { } last && Math.Abs(position - last) < 0.001) return position;
            previous = seeking ? null : position;
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("暂停后的原生定位未稳定。");
    }
}
