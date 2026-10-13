using System.Reflection;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendFaultTests
{
    [Theory]
    [InlineData(false, PlayerErrorKind.Session)]
    [InlineData(true, PlayerErrorKind.Session)]
    [InlineData(true, PlayerErrorKind.EventStream)]
    public async Task Shutdown_should_remain_terminal_after_late_success_and_media_events(bool withError, PlayerErrorKind kind)
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        StartPlaying(backend);
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "paused-for-cache", true));
        backend.OnSessionEvent(new(MpvEventId.Shutdown,
            Error: withError ? new InvalidOperationException("会话失败") : null, ErrorKind: kind));
        StartPlaying(backend);
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "time-pos", 42.0));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "paused-for-cache", true));
        backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 0));
        await backend.DisposeAsync();

        var events = await ReadAllAsync(backend);
        var fault = events.OfType<BackendFaulted>().Should().ContainSingle().Which;
        fault.Kind.Should().Be(kind);
        fault.IsTerminal.Should().BeTrue();
        events.Skip(events.IndexOf(fault) + 1).Should().BeEmpty("终态不再接受迟到的成功或媒体事件");
        events.OfType<PlaybackStateChanged>().Last().State.IsPlaying.Should().BeFalse();
        events.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(true, false);
        events.OfType<EndReached>().Should().BeEmpty();
    }

    [Fact]
    public async Task Shutdown_should_reject_initialization_and_all_playback_mutations()
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        StartPlaying(backend);
        backend.OnSessionEvent(new(MpvEventId.Shutdown));
        Func<Task>[] commands =
        [
            () => backend.InitializeAsync(CancellationToken.None),
            () => backend.LoadUrlAsync("http://127.0.0.1/media.wav", CancellationToken.None),
            () => backend.PlayAsync(CancellationToken.None),
            () => backend.PauseAsync(CancellationToken.None),
            () => backend.SeekAsync(1, CancellationToken.None),
            () => backend.SetPositionAsync(1, CancellationToken.None),
            () => backend.SetVolumeAsync(50, CancellationToken.None),
            () => backend.SetMuteAsync(true, CancellationToken.None),
            () => backend.SetAudioTrackAsync(1, CancellationToken.None),
            () => backend.SetSubtitleTrackAsync(1, CancellationToken.None),
        ];
        foreach (var command in commands)
            await command.Should().ThrowAsync<InvalidOperationException>().WithMessage("*重新启动*");
    }

    [Theory]
    [InlineData(false, PlayerErrorKind.Loading)]
    [InlineData(true, PlayerErrorKind.Playback)]
    public async Task Media_errors_should_distinguish_loading_from_playback(bool loaded, PlayerErrorKind expectedKind)
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        if (loaded) backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 4, Error: new InvalidOperationException("媒体读取失败")));
        await backend.DisposeAsync();

        var fault = (await ReadAllAsync(backend)).OfType<BackendFaulted>().Should().ContainSingle().Which;
        fault.Kind.Should().Be(expectedKind);
        fault.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task Native_initialization_failure_should_be_terminal_and_classified()
    {
        await using var session = new MpvPlayerSession(new Dictionary<string, string>
        {
            ["mpvshell-invalid-option"] = "yes",
        });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Func<Task> initialize = () => backend.InitializeAsync(timeout.Token);
        await initialize.Should().ThrowAsync<MpvException>();
        var events = await ReadUntilFaultAsync(backend, timeout.Token);
        var fault = events.OfType<BackendFaulted>().Should().ContainSingle().Which;
        fault.Kind.Should().Be(PlayerErrorKind.Initialization);
        fault.IsTerminal.Should().BeTrue();
        Func<Task> retry = () => backend.InitializeAsync(timeout.Token);
        await retry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*重新启动*");
    }

    [Fact]
    public async Task Native_shutdown_should_stop_the_session_and_reject_further_native_requests()
    {
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await backend.InitializeAsync(timeout.Token);
        var quit = session.CommandAsync(["quit"], timeout.Token);
        var events = await ReadUntilFaultAsync(backend, timeout.Token);
        events.OfType<BackendFaulted>().Should().ContainSingle().Which.Kind.Should().Be(PlayerErrorKind.Session);
        try { await quit; }
        catch (ObjectDisposedException) { } // quit 可以先收到命令回复，也可以先关闭会话。

        Func<Task> request = () => session.GetPropertyAsync("pause", timeout.Token);
        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("*已经停止*");
    }

    [Fact]
    public async Task Queue_overflow_should_stop_playback_until_a_new_file_starts()
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        StartPlaying(backend);
        backend.OnSessionEvent(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("队列溢出")));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "pause", false));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "idle-active", false));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "time-pos", 12.0));
        StartPlaying(backend);
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "time-pos", 13.0));
        await backend.DisposeAsync();

        var events = await ReadAllAsync(backend);
        var fault = events.OfType<BackendFaulted>().Should().ContainSingle().Which;
        fault.Kind.Should().Be(PlayerErrorKind.EventQueue);
        fault.IsTerminal.Should().BeFalse();
        events.OfType<PlaybackStateChanged>().Where(item => item.State.PositionSeconds == 12)
            .Should().NotBeEmpty().And.OnlyContain(item => !item.State.IsPlaying);
        events.OfType<PlaybackStateChanged>().Last().State.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task Queue_overflow_should_release_a_pending_load_and_allow_loading_another_media()
    {
        await using var held = new HttpMediaServer(holdMedia: true);
        await using var next = new HttpMediaServer();
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await backend.InitializeAsync(timeout.Token);
        var load = backend.LoadUrlAsync(held.MediaUrl, timeout.Token);
        await held.MediaRequested.WaitAsync(timeout.Token);
        backend.OnSessionEvent(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("队列溢出")));
        Func<Task> wait = () => load.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
        await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("队列溢出");

        await backend.LoadUrlAsync(next.MediaUrl, timeout.Token);
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(next.MediaUrl);
        await backend.PauseAsync(timeout.Token);
        await backend.PlayAsync(timeout.Token);
    }

    [Fact]
    public async Task Native_queue_overflow_should_pause_actual_playback_and_allow_reload_to_resume()
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(false);

        // 使用现有提交回调在真正的事件线程驱动 Dispatch，覆盖原生暂停路径，
        // 既不绕过 Session 仅注入后端事件，也不为测试扩大会话公共 API。
        var dispatch = typeof(MpvPlayerSession).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await session.CommandAsync(["seek", "0", "relative+exact"], timeout.Token,
            () => dispatch.Invoke(session, [new MpvEvent { Id = MpvEventId.QueueOverflow }]));
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(true,
            "状态失去可靠性、UI 等待重新加载时，原生 core 也应暂停播放");
        var fault = (await ReadUntilFaultAsync(backend, timeout.Token)).OfType<BackendFaulted>().Should().ContainSingle().Which;
        fault.Kind.Should().Be(PlayerErrorKind.EventQueue);
        fault.IsTerminal.Should().BeFalse();

        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(false);
        await backend.PauseAsync(timeout.Token);
        await backend.PlayAsync(timeout.Token);
        (await session.GetPropertyAsync("pause", timeout.Token)).Should().Be(false);
    }

    private static void StartPlaying(LibMpvBackend backend)
    {
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "pause", false));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "idle-active", false));
    }

    private static async Task<List<PlayerEvent>> ReadAllAsync(LibMpvBackend backend)
    {
        var events = new List<PlayerEvent>();
        await foreach (var item in backend.ObserveEventsAsync(CancellationToken.None)) events.Add(item);
        return events;
    }

    private static async Task<List<PlayerEvent>> ReadUntilFaultAsync(LibMpvBackend backend, CancellationToken cancellationToken)
    {
        var events = new List<PlayerEvent>();
        await foreach (var item in backend.ObserveEventsAsync(cancellationToken))
        {
            events.Add(item);
            if (item is BackendFaulted) return events;
        }
        throw new InvalidOperationException("事件流在故障到达前结束。");
    }
}
