using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class RenderingFailureIsolationTests
{
    [Fact]
    public async Task Fatal_pause_should_reject_playback_commands_already_queued_behind_the_recovery_lease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("pause", false, timeout.Token);
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            // 空闲恢复租约持锁，确保这些请求在终态建立前已经依次进入等待队列。
            var fatalPause = session.PauseForRenderingFailureAsync(timeout.Token).AsTask();
            Task<object?>[] blocked =
            [
                session.SetPropertyAsync("pause", false, timeout.Token),
                session.SetPropertyAsync("vid", "auto", timeout.Token),
                session.CommandAsync(["loadfile", "must-not-be-loaded.wav", "append"], timeout.Token),
                session.CommandAsync(["seek", "1", "absolute+exact"], timeout.Token),
                session.CommandAsync(["set", "pause", "no"], timeout.Token),
            ];
            var volume = session.SetPropertyAsync("volume", 37, timeout.Token);
            var mute = session.SetPropertyAsync("mute", true, timeout.Token);
            fatalPause.IsCompleted.Should().BeFalse();
            blocked.Should().OnlyContain(task => !task.IsCompleted);
            volume.IsCompleted.Should().BeFalse();
            mute.IsCompleted.Should().BeFalse();
            (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(false);

            await session.AbortVideoRecoveryAsync(lease);
            await fatalPause;
            foreach (var task in blocked)
            {
                Func<Task> command = async () => { await task; };
                // 检查会话层错误，防止 loadfile/seek 自身的原生失败造成假通过。
                await command.Should().ThrowExactlyAsync<InvalidOperationException>()
                    .WithMessage("视频渲染已停止，请重新打开播放器。");
            }
            await Task.WhenAll(volume, mute);
            (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true);
            Convert.ToInt64(await session.GetPropertyAsync("playlist-count", timeout.Token)).Should().Be(0);
            Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(37);
            (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }
    }

    [Fact]
    public async Task Fatal_session_should_keep_reads_sound_controls_and_shutdown_available_without_reopening_playback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.PauseForRenderingFailureAsync(timeout.Token);
        await session.PauseForRenderingFailureAsync(timeout.Token);
        await session.SetPropertyAsync("pause", true, timeout.Token);
        await session.SetPropertyAsync("volume", 41, timeout.Token);
        await session.SetPropertyAsync("mute", true, timeout.Token);

        var resume = () => session.SetPropertyAsync("pause", false, timeout.Token);
        await resume.Should().ThrowExactlyAsync<InvalidOperationException>();
        var changeTrack = () => session.SetPropertyAsync("aid", "auto", timeout.Token);
        await changeTrack.Should().ThrowExactlyAsync<InvalidOperationException>();
        var optionAlias = () => session.SetPropertyAsync("options/pause", false, timeout.Token);
        await optionAlias.Should().ThrowExactlyAsync<InvalidOperationException>();
        // 通用命令入口不解析放行例外；音量/静音只能通过明确的属性入口继续操作。
        var commandAlias = () => session.CommandAsync(["set", "volume", "99"], timeout.Token);
        await commandAlias.Should().ThrowExactlyAsync<InvalidOperationException>();

        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true);
        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(41);
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
        await session.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        var closed = () => session.GetPropertyAsync("pause", timeout.Token);
        await closed.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });
}
