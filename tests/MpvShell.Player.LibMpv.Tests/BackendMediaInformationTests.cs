using FluentAssertions;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendMediaInformationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disabling_audio_on_active_video_should_clear_codec_regardless_of_property_event_order(bool nullFirst)
    {
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "track-list", Tracks(audioSelected: true)));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "audio-codec-name", "aac"));
        if (nullFirst) backend.OnSessionEvent(new(MpvEventId.PropertyChange, "audio-codec-name", null));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "track-list", Tracks(audioSelected: false)));
        if (!nullFirst) backend.OnSessionEvent(new(MpvEventId.PropertyChange, "audio-codec-name", null));
        (await backend.GetInfoSnapshotAsync(CancellationToken.None)).AudioCodec.Should().BeNull();

        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "track-list", Tracks(audioSelected: true)));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "audio-codec-name", "pcm_s16le"));
        (await backend.GetInfoSnapshotAsync(CancellationToken.None)).AudioCodec.Should().Be("pcm_s16le");
    }

    [Fact]
    public async Task Eof_teardown_should_preserve_info_until_the_next_media_start()
    {
        await using var session = new MpvPlayerSession();
        await using var backend = new LibMpvBackend(session);
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        backend.OnSessionEvent(new(MpvEventId.FileLoaded));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "track-list", Tracks(audioSelected: true)));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "video-codec", "hevc"));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "audio-codec-name", "aac"));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "video-params", new Dictionary<string, object?>
        {
            ["w"] = 3840L, ["h"] = 2160L, ["gamma"] = "pq", ["pixelformat"] = "p010",
        }));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "cache-buffering-state", 0L));
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "frame-drop-count", 0L));
        var before = await backend.GetInfoSnapshotAsync(CancellationToken.None);
        before.CacheState.Should().Be("缓冲 0%");
        before.DecodeDiagnostics!.PresentationDroppedFrames.Should().Be(0);
        // 原生 EOF 先清空轨道和部分属性，再送达 EndFile；不能误判为用户关闭音轨。
        backend.OnSessionEvent(new(MpvEventId.PropertyChange, "track-list", Array.Empty<object?>()));
        foreach (var property in new[] { "audio-codec-name", "video-codec", "video-params", "cache-buffering-state", "frame-drop-count" })
            backend.OnSessionEvent(new(MpvEventId.PropertyChange, property, null));
        backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 0));
        (await backend.GetInfoSnapshotAsync(CancellationToken.None)).Should().Be(before);
        backend.OnSessionEvent(new(MpvEventId.StartFile));
        var reset = await backend.GetInfoSnapshotAsync(CancellationToken.None);
        reset.AudioCodec.Should().BeNull();
        reset.VideoCodec.Should().BeNull();
        reset.CacheState.Should().BeNull();
        reset.DynamicRange.Should().Be(VideoDynamicRange.Unknown);
        reset.DecodeDiagnostics.Should().BeNull();
        (await backend.GetTracksAsync(CancellationToken.None)).Should().BeEmpty();
    }

    private static object?[] Tracks(bool audioSelected) =>
    [
        new Dictionary<string, object?> { ["type"] = "video", ["id"] = 1L, ["selected"] = true },
        new Dictionary<string, object?> { ["type"] = "audio", ["id"] = 1L, ["selected"] = audioSelected },
    ];
}
