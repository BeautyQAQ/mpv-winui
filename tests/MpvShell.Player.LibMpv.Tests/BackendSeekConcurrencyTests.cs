using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendSeekConcurrencyTests
{
    [Fact]
    public async Task Rapid_absolute_seeks_should_settle_at_the_last_requested_position()
    {
        await using var media = new HttpMediaServer(durationSeconds: 60);
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await backend.PauseAsync(timeout.Token);

        // 并发调用不等待前一个 seek 回复；播放修改锁负责串行提交。
        var requests = new[] { 20.0, 5.0, 45.0, 10.0 }
            .Select(position => backend.SetPositionAsync(position, timeout.Token)).ToArray();
        await Task.WhenAll(requests);
        await WaitForPositionAsync(session, 10, timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true);
    }

    [Fact]
    public async Task Rapid_relative_seeks_should_accumulate_on_the_native_pending_target()
    {
        await using var media = new HttpMediaServer(durationSeconds: 60);
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await backend.PauseAsync(timeout.Token);
        await backend.SetPositionAsync(10, timeout.Token);
        await WaitForPositionAsync(session, 10, timeout.Token);

        var requests = new[] { 3.0, 4.0, -2.0 }
            .Select(delta => backend.SeekAsync(delta, timeout.Token)).ToArray();
        await Task.WhenAll(requests);
        await WaitForPositionAsync(session, 15, timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true);
    }

    private static async Task WaitForPositionAsync(MpvPlayerSession session, double expected, CancellationToken token)
    {
        // 命令回复表示请求已处理，实际落点仍以随后的原生属性为准。
        double position;
        do
        {
            position = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
            if (Math.Abs(position - expected) < 0.05) break;
            await Task.Delay(10, token);
        } while (true);
        position.Should().BeApproximately(expected, 0.05);
    }
}
