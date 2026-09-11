using System.Collections.Concurrent;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class MpvPlayerSessionTests
{
    [Fact]
    public async Task Native_session_should_configure_direct_hardware_policy_and_switch_color_targets()
    {
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await session.InitializeAsync(CancellationToken.None);
        (await session.GetPropertyAsync("options/hwdec", CancellationToken.None)).Should().BeEquivalentTo(new[] { "d3d11va" });
        (await session.GetPropertyAsync("options/gpu-hwdec-interop", CancellationToken.None)).Should().Be("d3d11-egl");
        Convert.ToDouble(await session.GetPropertyAsync("options/hr-seek-demuxer-offset", CancellationToken.None)).Should().Be(1);
        await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 1500, CancellationToken.None);
        (await session.GetPropertyAsync("options/target-trc", CancellationToken.None)).Should().Be("pq");
        (await session.GetPropertyAsync("options/target-prim", CancellationToken.None)).Should().Be("bt.2020");
        Convert.ToDouble(await session.GetPropertyAsync("options/target-peak", CancellationToken.None)).Should().Be(1500);
        await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Sdr, 1000, CancellationToken.None);
        (await session.GetPropertyAsync("options/target-trc", CancellationToken.None)).Should().Be("srgb");
        (await session.GetPropertyAsync("options/target-prim", CancellationToken.None)).Should().Be("bt.709");
        Convert.ToDouble(await session.GetPropertyAsync("options/target-peak", CancellationToken.None)).Should().Be(203);
    }

    [Fact]
    public async Task Native_empty_session_should_reject_play_without_claiming_playback()
    {
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(CancellationToken.None);
        var play = () => backend.PlayAsync(CancellationToken.None);
        await play.Should().ThrowAsync<InvalidOperationException>().WithMessage("请先打开媒体。");
    }

    [Fact]
    public async Task Native_sessions_should_initialize_query_and_dispose_repeatedly()
    {
        for (var cycle = 0; cycle < 100; cycle++)
        {
            var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
            await session.InitializeAsync(CancellationToken.None);
            await session.InitializeAsync(CancellationToken.None);
            await session.SetPropertyAsync("volume", 37, CancellationToken.None);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var values = await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => session.GetPropertyAsync("volume", CancellationToken.None)));
            values.Should().OnlyContain(value => Convert.ToDouble(value) == 37);

            var invalid = () => session.GetPropertyAsync("mpvshell-property-does-not-exist", CancellationToken.None);
            (await invalid.Should().ThrowAsync<MpvException>()).Which.ErrorCode.Should().Be(-8);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var cancel = () => session.SetPropertyAsync("volume", 89, canceled.Token);
            await cancel.Should().ThrowAsync<OperationCanceledException>();
            Convert.ToDouble(await session.GetPropertyAsync("volume", CancellationToken.None)).Should().Be(37);
            await session.DisposeAsync();
            await session.DisposeAsync();
            var closed = () => session.GetPropertyAsync("volume", CancellationToken.None);
            await closed.Should().ThrowAsync<ObjectDisposedException>();
        }
    }

    [Fact]
    public async Task Native_playback_should_report_time_pause_seek_tracks_and_eof()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mpvshell-native-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "中文 空格;音频.wav");
        WriteWave(file, 8);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var events = new ConcurrentQueue<PlayerEvent>();
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        var observer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in backend.ObserveEventsAsync(cancel.Token)) events.Enqueue(item);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        });
        try
        {
            await backend.InitializeAsync(cancel.Token);
            await backend.LoadUrlAsync(file, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>().Any(item => item.State.IsPlaying && item.State.PositionSeconds > 0.1), cancel.Token);
            events.OfType<PlaybackStateChanged>().Should().Contain(item => Math.Abs(item.State.DurationSeconds - 8) < 0.01);
            await WaitUntilAsync(() => events.OfType<TracksChanged>().Any(item => item.Tracks.Any(track => track.Kind == "audio")), cancel.Token);
            (await backend.GetTracksAsync(cancel.Token)).Should().ContainSingle(track => track.Kind == "audio");

            await backend.PauseAsync(cancel.Token);
            (await session.GetPropertyAsync("pause", cancel.Token)).Should().Be(true);
            await backend.SetPositionAsync(3, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>().Any(item => Math.Abs(item.State.PositionSeconds - 3) < 0.2), cancel.Token);
            await backend.SetVolumeAsync(41, cancel.Token);
            await backend.SetMuteAsync(true, cancel.Token);
            Convert.ToDouble(await session.GetPropertyAsync("volume", cancel.Token)).Should().Be(41);
            (await session.GetPropertyAsync("mute", cancel.Token)).Should().Be(true);
            await backend.PlayAsync(cancel.Token);
            await backend.SetPositionAsync(7.5, cancel.Token);
            await WaitUntilAsync(() => events.OfType<EndReached>().Any(), cancel.Token);
            events.Clear();
            await backend.PlayAsync(cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>().Any(item => item.State.IsPlaying && item.State.PositionSeconds > 0.1), cancel.Token);
            await backend.PauseAsync(cancel.Token);
            await backend.LoadUrlAsync(file, cancel.Token);
            (await session.GetPropertyAsync("pause", cancel.Token)).Should().Be(false);
            events.OfType<BackendFaulted>().Should().BeEmpty();
        }
        finally
        {
            cancel.Cancel();
            await observer;
            await backend.DisposeAsync();
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Native_loading_error_should_reach_application_events()
    {
        var file = Path.Combine(Path.GetTempPath(), "mpvshell-corrupt-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(file, [0, 255, 19, 87, 1, 0, 3]);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancel.Token);
            var load = () => backend.LoadUrlAsync(file, cancel.Token);
            (await load.Should().ThrowAsync<MpvException>()).Which.ErrorCode.Should().Be(-17);
            BackendFaulted? fault = null;
            await foreach (var item in backend.ObserveEventsAsync(cancel.Token))
                if (item is BackendFaulted error) { fault = error; break; }
            fault.Should().NotBeNull();
            fault!.Message.Should().Contain("加载或播放媒体");
            var play = () => backend.PlayAsync(cancel.Token);
            await play.Should().ThrowAsync<InvalidOperationException>().WithMessage("请先打开媒体。");
        }
        finally
        {
            await backend.DisposeAsync();
            File.Delete(file);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(20, cancellationToken);
    }

    private static void WriteWave(string path, int durationSeconds)
    {
        const int sampleRate = 44100;
        var dataLength = sampleRate * durationSeconds * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(dataLength + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }
}
