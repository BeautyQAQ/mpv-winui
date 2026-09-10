using System.Collections.Concurrent;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class LibMpvHttpTests
{
    [Fact]
    public async Task Http_media_should_load_pause_seek_and_reach_eof()
    {
        await using var server = new HttpMediaServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
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
            await backend.LoadUrlAsync(server.MediaUrl, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>()
                .Any(item => item.State.IsPlaying && item.State.PositionSeconds > 0.1), cancel.Token);
            Convert.ToDouble(await session.GetPropertyAsync("duration", cancel.Token)).Should().BeApproximately(8, 0.01);
            await backend.PauseAsync(cancel.Token);
            (await session.GetPropertyAsync("pause", cancel.Token)).Should().Be(true);
            await backend.SetPositionAsync(3, cancel.Token);
            await WaitUntilAsync(() => events.OfType<PlaybackStateChanged>()
                .Any(item => Math.Abs(item.State.PositionSeconds - 3) < 0.2), cancel.Token);
            await backend.PlayAsync(cancel.Token);
            await backend.SetPositionAsync(7.5, cancel.Token);
            await WaitUntilAsync(() => events.OfType<EndReached>().Any(), cancel.Token);

            server.ResponseCodes.Should().Contain(206);
            server.RangeRequests.Should().NotBeEmpty();
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
    public async Task Http_404_should_fail_the_load_and_publish_an_error_without_destroying_the_session()
    {
        await using var server = new HttpMediaServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(cancel.Token);

        var load = () => backend.LoadUrlAsync(server.MissingUrl, cancel.Token);
        await load.Should().ThrowAsync<MpvException>();
        BackendFaulted? fault = null;
        await foreach (var playerEvent in backend.ObserveEventsAsync(cancel.Token))
            if (playerEvent is BackendFaulted error) { fault = error; break; }
        fault.Should().NotBeNull();
        fault!.Message.Should().Contain("加载或播放媒体");
        server.ResponseCodes.Should().Contain(404);

        await backend.LoadUrlAsync(server.MediaUrl, cancel.Token);
        Convert.ToDouble(await session.GetPropertyAsync("duration", cancel.Token)).Should().BeApproximately(8, 0.01);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(20, cancellationToken);
    }
}
