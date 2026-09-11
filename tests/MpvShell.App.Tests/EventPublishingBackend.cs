using System.Threading.Channels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

internal sealed class EventPublishingBackend : IPlayerBackend
{
    private readonly Channel<PlayerEvent> _events = Channel.CreateUnbounded<PlayerEvent>();

    public CancellationToken ObservedCancellationToken { get; private set; }

    public Task InitializationCompletion { get; set; } = Task.CompletedTask;

    public Func<double, CancellationToken, Task>? SeekHandler { get; set; }

    public Func<double, CancellationToken, Task>? SetPositionHandler { get; set; }

    public Func<CancellationToken, Task>? PlayHandler { get; set; }

    public Func<int, CancellationToken, Task>? AudioTrackHandler { get; set; }

    public Func<int, CancellationToken, Task>? SubtitleTrackHandler { get; set; }

    public Func<string, CancellationToken, Task>? LoadHandler { get; set; }

    public TaskCompletionSource InitializationStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ObserveCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public void Publish(PlayerEvent playerEvent)
    {
        if (!_events.Writer.TryWrite(playerEvent))
            throw new InvalidOperationException("测试后端已结束，不能继续发布事件。");
    }

    public void FailEventObservation(Exception exception) => _events.Writer.TryComplete(exception);

    public IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(CancellationToken cancellationToken)
    {
        ObserveCalls++;
        ObservedCancellationToken = cancellationToken;
        return _events.Reader.ReadAllAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        InitializationStarted.TrySetResult();
        return InitializationCompletion;
    }
    public Task LoadUrlAsync(string url, CancellationToken cancellationToken) =>
        LoadHandler?.Invoke(url, cancellationToken) ?? Task.CompletedTask;
    public Task PlayAsync(CancellationToken cancellationToken) =>
        PlayHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) =>
        SeekHandler?.Invoke(deltaSeconds, cancellationToken) ?? Task.CompletedTask;
    public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) =>
        SetPositionHandler?.Invoke(absoluteSeconds, cancellationToken) ?? Task.CompletedTask;
    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) =>
        AudioTrackHandler?.Invoke(trackId, cancellationToken) ?? Task.CompletedTask;
    public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) =>
        SubtitleTrackHandler?.Invoke(trackId, cancellationToken) ?? Task.CompletedTask;

    public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

    public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));
}
