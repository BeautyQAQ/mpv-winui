using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendSeekabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seekable_property_before_or_after_file_loaded_should_enable_only_the_loaded_media(bool propertyFirst)
    {
        await using var events = new BackendEvents();
        events.Publish(new(MpvEventId.StartFile));
        events.Publish(new(MpvEventId.PropertyChange, "duration", 30.0));
        if (propertyFirst) events.Publish(new(MpvEventId.PropertyChange, "seekable", true));
        (await events.ReadStateAsync()).IsSeekable.Should().BeFalse("尚未加载完成的媒体不能接受时间轴输入");

        events.Publish(new(MpvEventId.FileLoaded));
        if (!propertyFirst) events.Publish(new(MpvEventId.PropertyChange, "seekable", true));
        (await events.ReadStateAsync()).IsSeekable.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task False_or_unavailable_seekability_should_disable_a_known_duration_media(bool? value)
    {
        await using var events = new BackendEvents();
        events.StartSeekableMedia();
        events.Publish(new(MpvEventId.PropertyChange, "duration", 30.0));
        (await events.ReadStateAsync()).IsSeekable.Should().BeTrue();

        events.Publish(new(MpvEventId.PropertyChange, "seekable", value));
        var state = await events.ReadStateAsync();
        state.DurationSeconds.Should().Be(30);
        state.IsSeekable.Should().BeFalse("已知时长不能替代后端定位能力");
    }

    [Fact]
    public async Task Seekability_should_remain_independent_of_duration_and_reset_when_another_file_starts()
    {
        await using var events = new BackendEvents();
        events.StartSeekableMedia();
        var first = await events.ReadStateAsync();
        first.IsSeekable.Should().BeTrue("时长由 UI 单独判断，能力只来自原生属性");
        first.DurationSeconds.Should().Be(0);

        events.Publish(new(MpvEventId.StartFile));
        events.Publish(new(MpvEventId.FileLoaded));
        events.Publish(new(MpvEventId.PropertyChange, "duration", 60.0));
        (await events.ReadStateAsync()).IsSeekable.Should().BeFalse("新媒体必须重新报告自己的定位能力");
    }

    [Theory]
    [InlineData((int)MpvEventId.EndFile, false)]
    [InlineData((int)MpvEventId.EndFile, true)]
    [InlineData((int)MpvEventId.QueueOverflow, true)]
    [InlineData((int)MpvEventId.Shutdown, true)]
    public async Task Failed_media_and_session_faults_should_clear_seekability_and_reject_late_success(int nativeEventId, bool loaded)
    {
        await using var backend = new LibMpvBackend(new MpvPlayerSession());
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "seekable", true));
        if (loaded) backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new((MpvEventId)nativeEventId, Value: 4, Error: new InvalidOperationException("测试媒体失败")));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "seekable", true));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        await backend.DisposeAsync();

        var states = new List<PlaybackState>();
        await foreach (var item in backend.ObserveEventsAsync(CancellationToken.None))
            if (item is PlaybackStateChanged change) states.Add(change.State);
        states.Should().NotBeEmpty();
        states.Last().IsSeekable.Should().BeFalse();
        states.Last().IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Unloaded_eof_should_disable_seekability_even_if_an_old_property_arrives_later()
    {
        await using var events = new BackendEvents();
        events.StartSeekableMedia();
        events.Publish(new(MpvEventId.PropertyChange, "duration", 30.0));
        (await events.ReadStateAsync()).IsSeekable.Should().BeTrue();

        events.Publish(new(MpvEventId.EndFile, Value: 0));
        events.Publish(new(MpvEventId.PropertyChange, "seekable", true));
        events.Publish(new(MpvEventId.FileLoaded));
        var ended = await events.ReadStateAsync();
        ended.IsSeekable.Should().BeFalse("已卸载媒体的 EOF 不能提供当前定位能力");
        ended.DurationSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Native_seekability_should_survive_kept_open_eof_and_allow_seeking_back()
    {
        await using var media = new HttpMediaServer(durationSeconds: 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = new MpvPlayerSession(new Dictionary<string, string>
        {
            ["ao"] = "null",
            ["keep-open"] = "yes",
        });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsSeekable: true }, timeout.Token);
        await backend.SetPositionAsync(1.8, timeout.Token);
        await ReadUntilAsync(backend, item => item is EndReached, timeout.Token);
        await backend.SetVolumeAsync(42, timeout.Token);
        var ended = (PlaybackStateChanged)await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 42 }, timeout.Token);
        ended.State.IsPlaying.Should().BeFalse();
        ended.State.IsSeekable.Should().BeTrue();
        (await session.GetPropertyAsync("seekable", timeout.Token)).Should().Be(true);

        await backend.SetPositionAsync(0.5, timeout.Token);
        var returned = (PlaybackStateChanged)await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.PositionSeconds: < 1.5 }, timeout.Token);
        returned.State.IsSeekable.Should().BeTrue();
    }

    [Fact]
    public async Task File_loaded_should_refresh_native_seekability_even_without_another_property_change()
    {
        await using var media = new HttpMediaServer(durationSeconds: 30);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(media.MediaUrl, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsSeekable: true }, timeout.Token);
        await backend.PauseAsync(timeout.Token);

        // 真实 core 中的媒体和 seekable=true 保持不变；仅重新送达生命周期，模拟 mpv
        // 合并两个相同观察值的情况。此时不会由属性变化再次提供 true，必须补读原生值。
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        await backend.SetVolumeAsync(45, timeout.Token);
        var starting = (PlaybackStateChanged)await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 45 }, timeout.Token);
        starting.State.IsSeekable.Should().BeFalse();
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsSeekable: true }, timeout.Token);
        (await session.GetPropertyAsync("seekable", timeout.Token)).Should().Be(true);
    }

    [Fact]
    public async Task Loading_a_replacement_and_a_native_load_failure_should_clear_previous_seekability()
    {
        await using var first = new HttpMediaServer(durationSeconds: 30);
        await using var held = new HttpMediaServer(holdMedia: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        await backend.LoadUrlAsync(first.MediaUrl, timeout.Token);
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsSeekable: true }, timeout.Token);

        var loading = backend.LoadUrlAsync(held.MediaUrl, timeout.Token);
        await held.MediaRequested.WaitAsync(timeout.Token);
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "seekable", true));
        await backend.SetVolumeAsync(43, timeout.Token);
        var pending = (PlaybackStateChanged)await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 43 }, timeout.Token);
        pending.State.IsSeekable.Should().BeFalse();
        held.ReleaseMedia();
        await loading;
        await ReadUntilAsync(backend, item => item is PlaybackStateChanged { State.IsSeekable: true }, timeout.Token);

        Func<Task> failedLoad = () => backend.LoadUrlAsync(first.MissingUrl, timeout.Token);
        await failedLoad.Should().ThrowAsync<MpvException>();
        await backend.SetVolumeAsync(44, timeout.Token);
        var failed = (PlaybackStateChanged)await ReadUntilAsync(backend,
            item => item is PlaybackStateChanged { State.Volume: 44 }, timeout.Token);
        failed.State.IsSeekable.Should().BeFalse();
    }

    private static async Task<PlayerEvent> ReadUntilAsync(LibMpvBackend backend,
        Func<PlayerEvent, bool> predicate, CancellationToken cancellationToken)
    {
        await foreach (var item in backend.ObserveEventsAsync(cancellationToken))
            if (predicate(item)) return item;
        throw new InvalidOperationException("事件流在预期状态到达前结束。");
    }

    private sealed class BackendEvents : IAsyncDisposable
    {
        private readonly LibMpvBackend _backend = new(new MpvPlayerSession());
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        private int _volume;

        internal void Publish(SessionEvent item) => _backend.OnSessionEvent(item);

        internal void StartSeekableMedia()
        {
            Publish(new(MpvEventId.StartFile));
            Publish(new(MpvEventId.FileLoaded));
            Publish(new(MpvEventId.PropertyChange, "seekable", true));
        }

        internal async Task<PlaybackState> ReadStateAsync()
        {
            var marker = ++_volume;
            Publish(new(MpvEventId.PropertyChange, "volume", (long)marker));
            return ((PlaybackStateChanged)await ReadUntilAsync(_backend,
                item => item is PlaybackStateChanged state && state.State.Volume == marker, _timeout.Token)).State;
        }

        public async ValueTask DisposeAsync()
        {
            await _backend.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
