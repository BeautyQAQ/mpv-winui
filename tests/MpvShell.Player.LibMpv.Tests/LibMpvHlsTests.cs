using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;

namespace MpvShell.Player.LibMpv.Tests;

/// <summary>Gate A 要求的 HLS 集成测试：本机播放列表与分片，不依赖公网地址。</summary>
public sealed class LibMpvHlsTests
{
    [Fact]
    public async Task Hls_playlist_should_load_pause_seek_across_segments_and_reach_eof()
    {
        await using var server = new HlsMediaServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        var events = new ConcurrentQueue<PlayerEvent>();
        var observer = Task.Run(async () =>
        {
            try
            {
                await foreach (var playerEvent in backend.ObserveEventsAsync(cancel.Token)) events.Enqueue(playerEvent);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        });

        try
        {
            await backend.InitializeAsync(cancel.Token);
            await backend.LoadUrlAsync(server.PlaylistUrl, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>()
                .Any(item => item.State.IsPlaying && item.State.PositionSeconds > 0.1), cancel.Token);
            Convert.ToDouble(await session.GetPropertyAsync("duration", cancel.Token)).Should().BeApproximately(server.TotalSeconds, 0.25);
            (await session.GetPropertyAsync("demuxer-via-network", cancel.Token)).Should().Be(true);
            (await session.GetPropertyAsync("file-format", cancel.Token)).Should().Be("hls");

            await backend.PauseAsync(cancel.Token);
            (await session.GetPropertyAsync("pause", cancel.Token)).Should().Be(true);
            // 跳到第三个分片中间，跨分片 seek 必须重新请求对应分片而不是从头解码。
            var target = server.SegmentSeconds * 2 + 0.5;
            await backend.SetPositionAsync(target, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>()
                .Any(item => Math.Abs(item.State.PositionSeconds - target) < 0.3), cancel.Token);
            await backend.PlayAsync(cancel.Token);
            await backend.SetPositionAsync(server.TotalSeconds - 0.5, cancel.Token);
            await WaitUntilAsync(() => events.OfType<EndReached>().Any(), cancel.Token);

            var requests = server.Requests.ToArray();
            requests.Should().Contain("/live/index.m3u8");
            for (var index = 0; index < server.SegmentCount; index++)
                requests.Should().Contain($"/live/segments/part{index}.aac", "每个分片都应通过 HTTP 取回");
            server.ResponseCodes.Should().NotContain(404).And.NotContain(416);
            events.OfType<TracksChanged>().Should().Contain(item => item.Tracks.Any(track => track.Kind == "audio"));
            events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally
        {
            cancel.Cancel();
            await observer;
            await backend.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hls_missing_playlist_should_fail_the_load_and_keep_the_session_usable()
    {
        await using var server = new HlsMediaServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(cancel.Token);

        var load = () => backend.LoadUrlAsync(server.MissingPlaylistUrl, cancel.Token);
        await load.Should().ThrowAsync<MpvException>();
        BackendFaulted? fault = null;
        await foreach (var playerEvent in backend.ObserveEventsAsync(cancel.Token))
            if (playerEvent is BackendFaulted error) { fault = error; break; }
        fault.Should().NotBeNull();
        fault!.Message.Should().Contain("加载或播放媒体");
        server.ResponseCodes.Should().Contain(404);

        await backend.LoadUrlAsync(server.PlaylistUrl, cancel.Token);
        Convert.ToDouble(await session.GetPropertyAsync("duration", cancel.Token)).Should().BeApproximately(server.TotalSeconds, 0.25);
    }

    [Fact]
    public async Task Hls_session_should_stay_in_process_without_launching_an_external_mpv()
    {
        await using var server = new HlsMediaServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        static int ExternalMpvProcesses() => Process.GetProcessesByName("mpv").Length;
        var before = ExternalMpvProcesses();
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(cancel.Token);
        await backend.LoadUrlAsync(server.PlaylistUrl, cancel.Token);

        using var current = Process.GetCurrentProcess();
        var modules = current.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToArray();
        modules.Should().Contain(name => name.Equals("libmpv-2.dll", StringComparison.OrdinalIgnoreCase),
            "播放会话必须由进程内 libmpv 承担");
        ExternalMpvProcesses().Should().Be(before, "不得启动外部 mpv.exe");
        (await session.GetPropertyAsync("file-format", cancel.Token)).Should().Be("hls");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(20, cancellationToken);
    }
}
