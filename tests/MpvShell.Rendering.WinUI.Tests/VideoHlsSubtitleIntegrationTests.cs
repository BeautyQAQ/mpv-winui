using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using MpvShell.Player.LibMpv;
using MpvShell.Player.LibMpv.Tests;
using static MpvShell.Rendering.WinUI.Tests.MediaAcceptanceFixture;

namespace MpvShell.Rendering.WinUI.Tests;

[Collection("HardwareMedia")]
public sealed class VideoHlsSubtitleIntegrationTests
{
    [MediaAcceptanceFact]
    public async Task Video_hls_should_render_pause_seek_across_segments_reach_eof_and_recover_from_http_errors()
    {
        await using var fixture = new MediaAcceptanceFixture("video-hls");
        await using var server = new HlsMediaServer(fixture.DirectoryPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var session = CreateSession(logLevel: "debug");
        await using var backend = new LibMpvBackend(session);
        fixture.Observe(backend, session);
        await backend.InitializeAsync(token);
        await using var graphics = new MediaFrameProbe(session);
        await graphics.InitializeAsync(token);
        fixture.Record("Adapter", graphics.Adapter);
        try
        {
            await backend.LoadUrlAsync(server.PlaylistUrl, token);
            await WaitPositionAsync(session, 0.5, token);
            (await session.GetPropertyAsync("file-format", token)).Should().Be("hls");
            (await session.GetPropertyAsync("demuxer-via-network", token)).Should().Be(true);
            var first = await graphics.CaptureAsync(token);
            await WaitPositionAsync(session, 1.2, token);
            var moving = await graphics.CaptureAsync(token);
            var movingPixels = MediaFrameProbe.ChangedPixels(first, moving);
            movingPixels.Should().BeGreaterThan(1000, "HLS 必须显示实际变化画面，不只推进时间");
            fixture.Record("MovingPixels", movingPixels);

            await SeekPausedAsync(backend, session, 4.5, token);
            var middle = await graphics.CaptureAsync(token);
            var pausedPosition = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
            await Task.Delay(200, token);
            var pausedAgain = await graphics.CaptureAsync(token);
            MediaFrameProbe.ChangedPixels(middle, pausedAgain).Should().BeLessThan(10);
            Convert.ToDouble(await session.GetPropertyAsync("time-pos", token)).Should().BeApproximately(pausedPosition, 0.001);
            await SeekPausedAsync(backend, session, 8.5, token);
            var later = await graphics.CaptureAsync(token);
            MediaFrameProbe.ChangedPixels(middle, later).Should().BeGreaterThan(1000);
            server.Requests.Should().Contain("/live/part02.ts").And.Contain("/live/part04.ts");
            var info = await backend.GetInfoSnapshotAsync(token);
            info.Resolution.Should().Be("320 × 180");
            info.VideoCodec.Should().Contain("H.264");
            info.AudioCodec.Should().Be("aac");
            info.DynamicRange.Should().Be(VideoDynamicRange.Sdr);
            info.CacheState.Should().NotBeNull();
            fixture.Record("PlayingInfo", info);

            await backend.SetPositionAsync(11.5, token);
            await backend.PlayAsync(token);
            await WaitUntilAsync(() => Task.FromResult(fixture.Events.OfType<EndReached>().Any()), token);
            await WaitUntilAsync(async () => await session.GetPropertyAsync("idle-active", token) is true, token);
            var ended = await backend.GetInfoSnapshotAsync(token);
            ended.VideoCodec.Should().Be(info.VideoCodec);
            ended.Resolution.Should().Be(info.Resolution);
            ended.DynamicRange.Should().Be(info.DynamicRange);
            fixture.Record("EofInfo", ended);
            fixture.Events.OfType<BackendFaulted>().Should().BeEmpty();

            foreach (var badUrl in new[] { server.MissingPlaylistUrl, server.BrokenSegmentPlaylistUrl })
            {
                var faultsBefore = fixture.Events.OfType<BackendFaulted>().Count();
                Func<Task> load = () => backend.LoadUrlAsync(badUrl, token);
                await load.Should().ThrowAsync<MpvException>();
                await WaitUntilAsync(() => Task.FromResult(fixture.Events.OfType<BackendFaulted>().Count() > faultsBefore), token);
                fixture.Events.OfType<BackendFaulted>().Last().Kind.Should().Be(PlayerErrorKind.Loading);
                await backend.LoadUrlAsync(server.PlaylistUrl, token);
                await WaitPositionAsync(session, 0.3, token);
                var recovered = await graphics.CaptureAsync(token);
                recovered.Where((_, index) => index % 4 != 3).Any(value => value > 100).Should().BeTrue();
            }
            server.ResponseCodes.Should().Contain(404);
            fixture.LogText.Should().Contain("[libmpv/").And.NotContain("private-hls-token")
                .And.NotContain("?token=", "实际网络诊断不能泄漏带签名的请求参数");
            fixture.Record("NativeLogRedaction", true);
            fixture.Record("Requests", server.Requests.ToArray());
            fixture.Record("ResponseCodes", server.ResponseCodes.ToArray());
        }
        finally { await session.CommandAsync(["stop"], CancellationToken.None); }
        fixture.Pass();
    }

    [MediaAcceptanceFact]
    public async Task Multitrack_selection_should_match_native_ids_languages_codecs_and_actual_subtitle_pixels()
    {
        await using var fixture = new MediaAcceptanceFixture("multitrack-subtitles");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        fixture.Observe(backend, session);
        await backend.InitializeAsync(token);
        await using var graphics = new MediaFrameProbe(session);
        await graphics.InitializeAsync(token);
        fixture.Record("Adapter", graphics.Adapter);
        try
        {
            await backend.LoadUrlAsync(fixture.FilePath("multitrack.mkv"), token);
            await SeekPausedAsync(backend, session, 3, token);
            await WaitUntilAsync(async () => (await backend.GetTracksAsync(token)).Count == 5, token);
            var tracks = await backend.GetTracksAsync(token);
            tracks.Where(track => track.Kind == "audio").Select(track => (track.Id, track.Language))
                .Should().Equal((1, "eng"), (2, "zho"));
            tracks.Where(track => track.Kind == "sub" && track.Id > 0).Select(track => (track.Id, track.Language))
                .Should().Equal((1, "eng"), (2, "zho"));
            tracks.Should().Contain(track => track.Title == "中文 880 Hz");
            fixture.Record("Tracks", tracks);

            foreach (var (id, codec) in new[] { (2, "pcm_s16le"), (1, "aac") })
            {
                await backend.SetAudioTrackAsync(id, token);
                await WaitUntilAsync(async () => (await backend.GetTracksAsync(token)).Any(track => track.Kind == "audio" && track.Id == id && track.Selected)
                    && (await backend.GetInfoSnapshotAsync(token)).AudioCodec == codec, token);
                (await session.GetPropertyAsync("aid", token)).Should().Be((long)id);
            }

            var beforeDisable = fixture.NativeEvents.Count;
            await backend.SetAudioTrackAsync(0, token);
            await WaitUntilAsync(async () => !(await backend.GetTracksAsync(token)).Any(track => track.Kind == "audio" && track.Selected), token);
            await WaitUntilAsync(() => Task.FromResult(fixture.NativeEvents.Skip(beforeDisable)
                .Any(item => item.Property == "audio-codec-name" && item.Value is null)), token);
            (await backend.GetInfoSnapshotAsync(token)).AudioCodec.Should().BeNull("当前媒体主动关闭音轨后不能仍显示旧编码");
            await backend.SetAudioTrackAsync(1, token);
            await WaitUntilAsync(async () => (await backend.GetInfoSnapshotAsync(token)).AudioCodec == "aac", token);

            await backend.SetSubtitleTrackAsync(0, token);
            await WaitTrackAsync(backend, 0, token);
            var baseline = await graphics.CaptureAsync(token);
            var metrics = new List<object>();
            foreach (var subtitleId in new[] { 1, 2 })
            {
                await backend.SetSubtitleTrackAsync(subtitleId, token);
                await WaitTrackAsync(backend, subtitleId, token);
                (await session.GetPropertyAsync("sid", token)).Should().Be((long)subtitleId);
                byte[] pixels = [];
                await WaitUntilAsync(async () =>
                {
                    pixels = await graphics.CaptureAsync(token);
                    return MediaFrameProbe.ChangedPixels(baseline, pixels) > 100;
                }, token);
                var topChanges = MediaFrameProbe.ChangedPixels(baseline, pixels, 0, 70);
                var bottomChanges = MediaFrameProbe.ChangedPixels(baseline, pixels, 100, MediaFrameProbe.Height);
                if (subtitleId == 1) bottomChanges.Should().BeGreaterThan(100, "SRT 文本应叠加在画面下方");
                else topChanges.Should().BeGreaterThan(100, "ASS 的顶端位置与样式必须实际生效");
                metrics.Add(new { SubtitleId = subtitleId, TopChanges = topChanges, BottomChanges = bottomChanges });
                await backend.SetSubtitleTrackAsync(0, token);
                await WaitTrackAsync(backend, 0, token);
                await WaitUntilAsync(async () => MediaFrameProbe.ChangedPixels(baseline, await graphics.CaptureAsync(token)) < 10, token);
            }
            fixture.Record("SubtitlePixels", metrics);
            fixture.Events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally { await session.CommandAsync(["stop"], CancellationToken.None); }
        fixture.Pass();
    }

    [MediaAcceptanceFact]
    public async Task Switching_media_should_clear_old_tracks_color_cache_and_codec_information()
    {
        await using var fixture = new MediaAcceptanceFixture("media-information");
        await using var server = new HlsMediaServer(fixture.DirectoryPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        fixture.Observe(backend, session);
        await backend.InitializeAsync(token);
        await using var graphics = new MediaFrameProbe(session);
        await graphics.InitializeAsync(token);
        try
        {
            await backend.LoadUrlAsync(server.PlaylistUrl, token);
            await WaitPositionAsync(session, 0.3, token);
            var networkInfo = await backend.GetInfoSnapshotAsync(token);
            networkInfo.CacheState.Should().NotBeNull();
            fixture.Record("NetworkInfo", networkInfo);
            await backend.LoadUrlAsync(fixture.FilePath("multitrack.mkv"), token);
            await WaitPositionAsync(session, 0.3, token);
            (await backend.GetTracksAsync(token)).Should().HaveCount(5);

            await backend.LoadUrlAsync(fixture.FilePath("audio-only.wav"), token);
            await WaitPositionAsync(session, 0.3, token);
            var audioInfo = await backend.GetInfoSnapshotAsync(token);
            fixture.Record("AudioOnlyInfo", audioInfo);
            audioInfo.VideoCodec.Should().BeNull();
            audioInfo.Resolution.Should().BeNull();
            audioInfo.BitDepth.Should().BeNull();
            audioInfo.HdrType.Should().BeNull();
            audioInfo.DynamicRange.Should().Be(VideoDynamicRange.Unknown);
            var nativeCache = await session.GetPropertyAsync("cache-buffering-state", token);
            fixture.Record("AudioOnlyNativeCache", nativeCache);
            audioInfo.CacheState.Should().Be(nativeCache is null ? null : $"缓冲 {Convert.ToDouble(nativeCache):0}%",
                "缓存快照必须对应当前媒体的原生值，不将本地文件的原生 100% 误判为遗留网络状态");
            audioInfo.AudioCodec.Should().Be("pcm_s16le");
            (await backend.GetTracksAsync(token)).Should().ContainSingle().Which.Kind.Should().Be("audio");

            await backend.LoadUrlAsync(fixture.FilePath("video-only.mkv"), token);
            await WaitPositionAsync(session, 0.3, token);
            var videoInfo = await backend.GetInfoSnapshotAsync(token);
            fixture.Record("VideoOnlyInfo", videoInfo);
            videoInfo.AudioCodec.Should().BeNull();
            videoInfo.Resolution.Should().Be("160 × 90");
            videoInfo.DynamicRange.Should().Be(VideoDynamicRange.Sdr);
            (await backend.GetTracksAsync(token)).Should().BeEmpty();
            fixture.Events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally { await session.CommandAsync(["stop"], CancellationToken.None); }
        fixture.Pass();
    }

    [MediaAcceptanceFact]
    public async Task A_held_video_segment_should_publish_real_buffering_and_resume_after_release()
    {
        await using var fixture = new MediaAcceptanceFixture("video-hls-buffering");
        await using var server = new HlsMediaServer(fixture.DirectoryPath, heldSegment: "part04.ts");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        fixture.Observe(backend, session);
        await backend.InitializeAsync(token);
        await using var graphics = new MediaFrameProbe(session);
        await graphics.InitializeAsync(token);
        try
        {
            await backend.LoadUrlAsync(server.PlaylistUrl, token);
            await WaitPositionAsync(session, 0.3, token);
            await server.SegmentRequested.WaitAsync(token);
            await WaitUntilAsync(async () => await session.GetPropertyAsync("paused-for-cache", token) is true
                && fixture.Events.OfType<BufferingChanged>().LastOrDefault()?.IsBuffering == true, token);
            var stalled = Convert.ToDouble(await session.GetPropertyAsync("time-pos", token));
            stalled.Should().BeInRange(1, 8.1);
            fixture.Record("BufferingPosition", stalled);
            server.ReleaseSegment();
            await WaitUntilAsync(async () => await session.GetPropertyAsync("paused-for-cache", token) is false
                && fixture.Events.OfType<BufferingChanged>().LastOrDefault()?.IsBuffering == false, token);
            await WaitPositionAsync(session, 8.5, token);
            var first = await graphics.CaptureAsync(token);
            await WaitPositionAsync(session, 9.2, token);
            MediaFrameProbe.ChangedPixels(first, await graphics.CaptureAsync(token)).Should().BeGreaterThan(1000);
            fixture.Record("BufferingTransitions", fixture.Events.OfType<BufferingChanged>().ToArray());
            fixture.Record("ResumedInfo", await backend.GetInfoSnapshotAsync(token));
            fixture.Events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally { server.ReleaseSegment(); await session.CommandAsync(["stop"], CancellationToken.None); }
        fixture.Pass();
    }

    private static MpvPlayerSession CreateSession(string logLevel = "error") => new(new Dictionary<string, string>
    {
        ["ao"] = "null", ["sub-auto"] = "no", ["audio-file-auto"] = "no", ["sub-font"] = "Arial",
    }, logLevel);

    private static Task WaitTrackAsync(LibMpvBackend backend, int id, CancellationToken token) =>
        WaitUntilAsync(async () => (await backend.GetTracksAsync(token)).Any(track => track.Kind == "sub" && track.Id == id && track.Selected), token);
}
