using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class BackendPlaylistRedirectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_playlist_redirect_should_wait_for_the_actual_media_to_load(bool nested)
    {
        using var media = new PlaylistMedia();
        var playlist = media.WritePlaylist("playlist.m3u", Path.GetFileName(media.WavePath));
        if (nested) playlist = media.WritePlaylist("outer.m3u", Path.GetFileName(playlist));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        var nativeEvents = new ConcurrentQueue<SessionEvent>();
        session.EventReceived += nativeEvents.Enqueue;
        await backend.InitializeAsync(timeout.Token);

        await backend.LoadUrlAsync(playlist, timeout.Token);

        nativeEvents.Should().Contain(item => item.Id == MpvEventId.EndFile && Equals(item.Value, 5),
            "a playlist expands through EndFile(redirect) before its media can load");
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.WavePath);
        Convert.ToDouble(await session.GetPropertyAsync("duration", timeout.Token)).Should().BeApproximately(8, 0.01);
        await backend.SetVolumeAsync(36, timeout.Token);
        var events = await ReadUntilVolumeAsync(backend, 36, timeout.Token);
        var state = events.OfType<PlaybackStateChanged>().Last().State;
        state.CurrentUrl.Should().Be(playlist);
        state.IsPlaying.Should().BeTrue();
        events.OfType<BackendFaulted>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_redirected_media_error_should_reach_the_load_caller_and_allow_a_later_load()
    {
        using var media = new PlaylistMedia();
        var corrupt = media.WriteCorruptMedia();
        var playlist = media.WritePlaylist("broken.m3u", Path.GetFileName(corrupt));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);

        var load = () => backend.LoadUrlAsync(playlist, timeout.Token);
        (await load.Should().ThrowAsync<MpvException>()).Which.ErrorCode.Should().Be(-17,
            "the redirected media's loading error must replace the intermediate redirect notification");

        await backend.LoadUrlAsync(media.WavePath, timeout.Token);
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.WavePath);
    }

    [Fact]
    public async Task An_empty_playlist_should_fail_without_waiting_for_the_load_timeout()
    {
        using var media = new PlaylistMedia();
        var playlist = media.WritePlaylist("empty.m3u");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = CreateSession();
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);

        var load = () => backend.LoadUrlAsync(playlist, timeout.Token);
        var failure = (await load.Should().ThrowAsync<Exception>()).Which;
        failure.Should().NotBeOfType<TimeoutException>();
        failure.Should().NotBeAssignableTo<OperationCanceledException>(
            "a redirect with no playable entries must fail immediately instead of waiting for FileLoaded");

        await backend.LoadUrlAsync(media.WavePath, timeout.Token);
        (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(media.WavePath);
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });

    private static async Task<List<PlayerEvent>> ReadUntilVolumeAsync(LibMpvBackend backend,
        int volume, CancellationToken cancellationToken)
    {
        var events = new List<PlayerEvent>();
        await foreach (var item in backend.ObserveEventsAsync(cancellationToken))
        {
            events.Add(item);
            if (item is PlaybackStateChanged state && state.State.Volume == volume) return events;
        }
        throw new InvalidOperationException("事件流在预期音量状态到达前结束。");
    }

    private sealed class PlaylistMedia : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "mpvshell-playlist-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _files = [];

        internal string WavePath { get; }

        internal PlaylistMedia()
        {
            Directory.CreateDirectory(_directory);
            WavePath = Path.Combine(_directory, "audio.wav");
            _files.Add(WavePath);
            const int sampleRate = 44100;
            const int dataLength = sampleRate * 8 * 2;
            using var writer = new BinaryWriter(File.Create(WavePath));
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

        internal string WritePlaylist(string name, params string[] entries)
        {
            var path = Path.Combine(_directory, name);
            _files.Add(path);
            File.WriteAllText(path, "#EXTM3U\n" + string.Join('\n', entries) + "\n", new UTF8Encoding(false));
            return path;
        }

        internal string WriteCorruptMedia()
        {
            var path = Path.Combine(_directory, "corrupt.wav");
            _files.Add(path);
            File.WriteAllBytes(path, [0, 255, 19, 87, 1, 0, 3]);
            return path;
        }

        public void Dispose()
        {
            foreach (var file in _files) File.Delete(file);
            Directory.Delete(_directory);
        }
    }
}
