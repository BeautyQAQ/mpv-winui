using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

/// <summary>页面卸载挂起（SuspendVideoForDetachAsync / ResumeVideoAfterDetachAsync）的会话层契约。</summary>
public sealed class VideoDetachLeaseTests
{
    [Fact]
    public async Task Detach_without_media_should_not_hold_the_playback_lease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SuspendVideoForDetachAsync(timeout.Token);
        // 无媒体可挂起时必须是 no-op：不能把播放修改租约留到下一次恢复。
        await session.SetPropertyAsync("volume", 37, timeout.Token);
        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(37);
        await session.ResumeVideoAfterDetachAsync(timeout.Token);
        await session.SetPropertyAsync("mute", true, timeout.Token);
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
    }

    [Fact]
    public async Task Repeated_detach_and_resume_without_media_should_stay_noops()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SuspendVideoForDetachAsync(timeout.Token);
        await session.SuspendVideoForDetachAsync(timeout.Token);
        await session.ResumeVideoAfterDetachAsync(timeout.Token);
        await session.ResumeVideoAfterDetachAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 41, timeout.Token);
        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(41);
    }

    [Fact]
    public async Task Detach_after_terminal_failure_should_not_hold_the_lease_or_reopen_playback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 19, timeout.Token);
        await session.PauseForRenderingFailureAsync(timeout.Token);
        await session.SuspendVideoForDetachAsync(timeout.Token);
        await session.ResumeVideoAfterDetachAsync(timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true);
        // 被终态封锁的播放修改必须立即失败（租约未被挂起占用，否则这里会一直等待直到取消）。
        var resume = () => session.SetPropertyAsync("pause", false, timeout.Token);
        await resume.Should().ThrowExactlyAsync<InvalidOperationException>();
        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19);
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });
}
