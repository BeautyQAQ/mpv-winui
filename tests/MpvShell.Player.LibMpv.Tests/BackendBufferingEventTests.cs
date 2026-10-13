using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendBufferingEventTests
{
    [Fact]
    public async Task Overflow_should_deliver_a_repeated_identical_error_once_per_occurrence()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        for (var occurrence = 0; occurrence < 2; occurrence++)
        {
            events.Publish(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("相同错误")));
            var end = 513 + occurrence * 512;
            for (var position = end - 511; position <= end; position++) events.Position(position);
            (await events.ReadThroughPositionAsync(end)).OfType<BackendFaulted>()
                .Should().Equal(new BackendFaulted("相同错误", PlayerErrorKind.EventQueue));
        }

        for (var position = 1026; position <= 1537; position++) events.Position(position);
        (await events.ReadThroughPositionAsync(1537)).OfType<BackendFaulted>().Should().BeEmpty(
            "已交付错误不能在下一次积压时重新弹出");
    }

    [Fact]
    public async Task Overflow_should_restore_the_latest_tracks_info_and_error_in_publication_order()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(new(MpvEventId.PropertyChange, "track-list", new object?[]
        {
            new Dictionary<string, object?> { ["type"] = "audio", ["id"] = 2L, ["lang"] = "zho", ["selected"] = true },
        }));
        events.Publish(new(MpvEventId.PropertyChange, "video-codec", "hevc"));
        events.Buffer(true);
        events.Publish(new(MpvEventId.EndFile, Value: 4, Error: new InvalidOperationException("读取失败")));
        for (var position = 2; position <= 513; position++) events.Position(position);

        var resumed = await events.ReadThroughPositionAsync(513);
        resumed.OfType<TracksChanged>().Should().ContainSingle().Which.Tracks.Should().ContainSingle()
            .Which.Id.Should().Be(2);
        resumed.OfType<MediaInfoChanged>().Should().ContainSingle().Which.Snapshot.VideoCodec.Should().Be("hevc");
        resumed.OfType<BackendFaulted>().Should().Equal(new BackendFaulted("读取失败"));
        var trackIndex = resumed.FindIndex(item => item is TracksChanged);
        var infoIndex = resumed.FindIndex(item => item is MediaInfoChanged);
        var faultIndex = resumed.FindIndex(item => item is BackendFaulted);
        trackIndex.Should().BeLessThan(infoIndex);
        infoIndex.Should().BeLessThan(faultIndex);
        faultIndex.Should().BeLessThan(resumed.FindIndex(item => item is PlaybackStateChanged));
        resumed.OfType<PlaybackStateChanged>().Should().HaveCount(256);
    }

    [Fact]
    public async Task Overflow_should_deliver_eof_once_and_not_replay_it_after_seeking_back()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);
        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", true));
        for (var position = 2; position <= 513; position++) events.Position(position);
        (await events.ReadThroughPositionAsync(513)).OfType<EndReached>().Should().ContainSingle();

        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", false));
        for (var position = 514; position <= 1025; position++) events.Position(position);
        (await events.ReadThroughPositionAsync(1025)).OfType<EndReached>().Should().BeEmpty();
        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", true));
        for (var position = 1026; position <= 1537; position++) events.Position(position);
        (await events.ReadThroughPositionAsync(1537)).OfType<EndReached>().Should().ContainSingle();
    }

    [Fact]
    public async Task Overflow_should_restore_a_stopped_playback_state_even_when_only_info_events_survive()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Publish(new(MpvEventId.PropertyChange, "pause", false));
        events.Position(1);
        await events.ReadThroughPositionAsync(1);
        events.Publish(new(MpvEventId.EndFile, Value: 4, Error: new InvalidOperationException("读取失败")));
        for (var frame = 1; frame <= 512; frame++)
            events.Publish(new(MpvEventId.PropertyChange, "frame-drop-count", (long)frame));

        var resumed = await events.ReadUntilAsync(item => item is MediaInfoChanged
            { Snapshot.DecodeDiagnostics.PresentationDroppedFrames: 512 }, "末次诊断信息");
        resumed.OfType<BackendFaulted>().Should().ContainSingle();
        resumed.OfType<PlaybackStateChanged>().Should().ContainSingle().Which.State.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Overflow_repair_should_use_the_surviving_event_snapshot_instead_of_a_later_error()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);
        events.Publish(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("第一次错误")));
        for (var position = 2; position <= 513; position++) events.Position(position);
        events.Publish(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("第二次错误")));
        events.Position(514);

        var resumed = await events.ReadThroughPositionAsync(514);
        resumed.OfType<BackendFaulted>().Select(item => item.Message).Should().Equal("第一次错误", "第二次错误");
        resumed.First().Should().Be(new BackendFaulted("第一次错误", PlayerErrorKind.EventQueue));
        resumed.FindLastIndex(item => item is BackendFaulted).Should()
            .BeGreaterThan(resumed.FindIndex(item => item is PlaybackStateChanged { State.PositionSeconds: 513 }),
                "第二次错误不能提前到先前的位置事件之前；错误之后还会发布同位置的停止快照");
    }

    [Fact]
    public async Task Overflow_after_replacing_media_should_not_restore_the_previous_error_or_eof()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);
        events.Publish(new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("旧媒体错误")));
        events.Publish(new(MpvEventId.EndFile, Value: 0));
        events.StartMedia();
        for (var position = 2; position <= 513; position++) events.Position(position);

        var resumed = await events.ReadThroughPositionAsync(513);
        resumed.OfType<BackendFaulted>().Should().BeEmpty();
        resumed.OfType<EndReached>().Should().BeEmpty();
        resumed.OfType<TracksChanged>().Should().ContainSingle().Which.Tracks.Should().BeEmpty();
        resumed.OfType<MediaInfoChanged>().Should().ContainSingle().Which.Snapshot.VideoCodec.Should().BeNull();
    }

    [Fact]
    public async Task Cache_pause_should_publish_both_transitions_without_requiring_another_event()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Buffer(true);
        (await events.ReadNextAsync()).Should().Be(new BufferingChanged(true));
        events.Buffer(false);
        (await events.ReadNextAsync()).Should().Be(new BufferingChanged(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overflow_should_restore_latest_buffering_before_the_next_surviving_event(bool latest)
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(!latest);
        events.Position(1);
        var initial = await events.ReadThroughPositionAsync(1);
        (initial.OfType<BufferingChanged>().LastOrDefault()?.IsBuffering ?? false).Should().Be(!latest);

        events.Buffer(latest);
        // The only notification for the new value is evicted while the consumer is suspended.
        for (var position = 2; position <= 513; position++) events.Position(position);

        var resumed = await events.ReadThroughPositionAsync(513);
        resumed.First().Should().Be(new BufferingChanged(latest));
        resumed.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(latest);
        resumed.OfType<PlaybackStateChanged>().Should().HaveCount(256,
            "the telemetry queue must remain bounded even when its consumer stalls");
        resumed.OfType<PlaybackStateChanged>().First().State.PositionSeconds.Should().Be(258);
    }

    [Fact]
    public async Task Overflow_repair_should_preserve_the_order_of_surviving_buffering_and_playback_states()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Buffer(false);
        for (var position = 2; position <= 513; position++) events.Position(position);
        events.Buffer(true);
        events.Position(514);

        var resumed = await events.ReadThroughPositionAsync(514);
        resumed.First().Should().Be(new BufferingChanged(false),
            "older retained events precede the latest true value even though the global snapshot is already true");
        var isBuffering = true;
        foreach (var item in resumed)
        {
            if (item is BufferingChanged buffering) isBuffering = buffering.IsBuffering;
            if (item is PlaybackStateChanged playback)
                isBuffering.Should().Be(playback.State.PositionSeconds == 514);
        }
        resumed.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false, true);
    }

    [Fact]
    public async Task Starting_another_media_should_clear_previous_buffering_before_its_state_events()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(new(MpvEventId.StartFile));
        events.Position(2);
        var switched = await events.ReadThroughPositionAsync(2);

        switched.First().Should().Be(new BufferingChanged(false));
        switched.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false);
        switched.Should().Contain(item => item is TracksChanged);
        events.Buffer(true);
        (await events.ReadNextAsync()).Should().Be(new BufferingChanged(true),
            "the new media may start buffering before FileLoaded");
    }

    [Theory]
    [InlineData(0)] // EOF
    [InlineData(2)] // stop / replacement
    [InlineData(3)] // quit
    [InlineData(4)] // error
    [InlineData(5)] // redirect
    public async Task Every_end_reason_should_reset_buffering_and_ignore_late_cache_properties(int reason)
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(new(MpvEventId.EndFile, Value: reason,
            Error: reason == 4 ? new InvalidOperationException("media read failed") : null));
        events.Buffer(true);
        events.Position(2);
        var ended = await events.ReadThroughPositionAsync(2);

        ended.First().Should().Be(new BufferingChanged(false));
        ended.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false);
        ended.OfType<PlaybackStateChanged>().Should().OnlyContain(item => !item.State.IsPlaying);

        events.StartMedia();
        events.Buffer(true);
        events.Position(3);
        (await events.ReadThroughPositionAsync(3)).OfType<BufferingChanged>()
            .Select(item => item.IsBuffering).Should().Equal(true);
    }

    [Fact]
    public async Task End_reset_should_survive_overflow_without_stale_buffering_reappearing()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(new(MpvEventId.EndFile, Value: 2));
        events.Buffer(true);
        for (var position = 2; position <= 513; position++) events.Position(position);

        var ended = await events.ReadThroughPositionAsync(513);
        ended.First().Should().Be(new BufferingChanged(false));
        ended.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false);
    }

    [Fact]
    public async Task Kept_open_eof_should_clear_buffering_and_allow_buffering_again_after_seek()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", true));
        events.Buffer(true);
        events.Position(2);
        var ended = await events.ReadThroughPositionAsync(2);
        ended.First().Should().Be(new BufferingChanged(false));
        ended.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false);
        ended.OfType<EndReached>().Should().ContainSingle();

        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", false));
        events.Buffer(true);
        events.Position(3);
        (await events.ReadThroughPositionAsync(3)).OfType<BufferingChanged>()
            .Select(item => item.IsBuffering).Should().Equal(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_or_fault_should_clear_buffering_before_the_fault_and_reject_late_properties(bool fault)
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        events.Publish(fault
            ? new(MpvEventId.QueueOverflow, Error: new InvalidOperationException("native event queue overflow"))
            : new(MpvEventId.Shutdown));
        events.Buffer(true);
        events.Position(2);

        await events.Backend.DisposeAsync();
        var ended = new List<PlayerEvent>();
        while (await events.ReadNextAsync() is { } item) ended.Add(item);
        ended.First().Should().Be(new BufferingChanged(false));
        ended.OfType<BufferingChanged>().Select(item => item.IsBuffering).Should().Equal(false);
        ended.OfType<BackendFaulted>().Should().ContainSingle();
    }

    [Fact]
    public async Task Disposal_should_deliver_buffering_reset_before_completing_the_event_stream()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Buffer(true);
        events.Position(1);
        await events.ReadThroughPositionAsync(1);

        await events.Backend.DisposeAsync();
        events.Buffer(true);
        (await events.ReadNextAsync()).Should().Be(new BufferingChanged(false));
        (await events.ReadNextAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Eof_should_align_position_with_duration_instead_of_the_last_frame_timestamp()
    {
        // mpv 的 time-pos 停在最后一帧（8 秒 30 fps 视频约 7.967 秒），UI 向下取整会停在 00:07。
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Publish(new(MpvEventId.PropertyChange, "duration", 8.0));
        events.Position(7.967);
        await events.ReadThroughPositionAsync(7.967);

        events.Publish(new(MpvEventId.EndFile, Value: 0));
        events.Publish(new(MpvEventId.PropertyChange, "volume", 55.0));
        var ended = await events.ReadUntilAsync(item => item is PlaybackStateChanged { State.Volume: 55 }, "volume 55");

        ended.OfType<EndReached>().Should().ContainSingle();
        ended.OfType<PlaybackStateChanged>().Should().OnlyContain(item => item.State.PositionSeconds == 8 && !item.State.IsPlaying,
            "the ended state must report the full duration, and later state updates must keep it");
    }

    [Fact]
    public async Task Kept_open_eof_should_align_position_with_duration_and_accept_positions_again_after_seeking_back()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Publish(new(MpvEventId.PropertyChange, "pause", false));
        events.Publish(new(MpvEventId.PropertyChange, "duration", 8.0));
        events.Position(7.967);
        await events.ReadThroughPositionAsync(7.967);

        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", true));
        var ended = await events.ReadThroughPositionAsync(8);
        ended.OfType<EndReached>().Should().ContainSingle();
        ended.OfType<PlaybackStateChanged>().Last().State.IsPlaying.Should().BeFalse();

        events.Publish(new(MpvEventId.PropertyChange, "eof-reached", false));
        events.Position(3);
        (await events.ReadThroughPositionAsync(3)).OfType<PlaybackStateChanged>().Last().State.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task Eof_without_a_known_duration_should_keep_the_last_reported_position()
    {
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Position(7.967);
        await events.ReadThroughPositionAsync(7.967);

        events.Publish(new(MpvEventId.EndFile, Value: 0));
        var ended = await events.ReadUntilAsync(item => item is PlaybackStateChanged { State.IsPlaying: false }, "the ended state");
        ended.OfType<EndReached>().Should().ContainSingle();
        ended.OfType<PlaybackStateChanged>().Last().State.PositionSeconds.Should().Be(7.967);
    }

    [Fact]
    public async Task Eof_should_keep_the_last_known_media_info_until_the_next_file_starts()
    {
        // mpv 在 EndFile 之前卸载视频链，把 video-params、hwdec-current 等逐项清成 null。
        await using var events = new BackendEvents();
        events.StartMedia();
        events.Publish(new(MpvEventId.PropertyChange, "video-codec", "hevc"));
        events.Publish(new(MpvEventId.PropertyChange, "hwdec-current", "d3d11va"));
        events.Publish(new(MpvEventId.PropertyChange, "frame-drop-count", 15L));
        events.Publish(new(MpvEventId.PropertyChange, "video-params",
            new Dictionary<string, object?> { ["w"] = 3840L, ["h"] = 2160L, ["gamma"] = "pq", ["pixelformat"] = "d3d11" }));
        events.Position(1);
        var loaded = await events.ReadThroughPositionAsync(1);
        loaded.OfType<MediaInfoChanged>().Last().Snapshot.Resolution.Should().Be("3840 × 2160");

        events.Publish(new(MpvEventId.PropertyChange, "video-params", null));
        events.Publish(new(MpvEventId.PropertyChange, "video-codec", null));
        events.Publish(new(MpvEventId.PropertyChange, "hwdec-current", null));
        events.Publish(new(MpvEventId.PropertyChange, "frame-drop-count", null));
        events.Publish(new(MpvEventId.EndFile, Value: 0));
        var ended = await events.ReadUntilAsync(item => item is EndReached, "EndReached");
        ended.OfType<MediaInfoChanged>().Should().BeEmpty("clearing properties at EOF must not downgrade the panel");

        events.Publish(new(MpvEventId.StartFile));
        var restarted = await events.ReadUntilAsync(item => item is MediaInfoChanged, "MediaInfoChanged after StartFile");
        restarted.OfType<MediaInfoChanged>().Single().Snapshot.Resolution.Should().BeNull("a new file starts from an empty panel");
    }

    private sealed class BackendEvents : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        private readonly IAsyncEnumerator<PlayerEvent> _reader;

        internal LibMpvBackend Backend { get; } = new(new MpvPlayerSession());

        internal BackendEvents() => _reader = Backend.ObserveEventsAsync(_timeout.Token).GetAsyncEnumerator();

        internal void StartMedia()
        {
            Publish(new(MpvEventId.StartFile));
            Publish(new(MpvEventId.FileLoaded));
        }

        internal void Publish(SessionEvent item) => Backend.OnSessionEvent(item);
        internal void Buffer(bool value) => Publish(new(MpvEventId.PropertyChange, "paused-for-cache", value));
        internal void Position(double value) => Publish(new(MpvEventId.PropertyChange, "time-pos", value));

        internal async Task<PlayerEvent?> ReadNextAsync() => await _reader.MoveNextAsync() ? _reader.Current : null;

        internal Task<List<PlayerEvent>> ReadThroughPositionAsync(double position) =>
            ReadUntilAsync(item => item is PlaybackStateChanged state && state.State.PositionSeconds == position,
                $"position {position}");

        internal async Task<List<PlayerEvent>> ReadUntilAsync(Func<PlayerEvent, bool> predicate, string description)
        {
            var result = new List<PlayerEvent>();
            while (await ReadNextAsync() is { } item)
            {
                result.Add(item);
                if (predicate(item)) return result;
            }
            throw new InvalidOperationException($"The event stream ended before {description}.");
        }

        public async ValueTask DisposeAsync()
        {
            await _reader.DisposeAsync();
            await Backend.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
