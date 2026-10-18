using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel
{
    private CancellationTokenSource _inputCts = new();
    private long _volumeVersion;

    public bool CanSeek => !_disposed && !HasTerminalFailure && !IsStopping && !IsLoading && PlaybackInteractionCoordinator.CanSeek(State);
    private bool HasKnownDuration => double.IsFinite(State.DurationSeconds) && State.DurationSeconds > 0;
    public double TimelineMaximum => HasKnownDuration ? State.DurationSeconds : 1;
    public double TimelineValue => HasKnownDuration
        ? PlaybackInteractionCoordinator.ClampPosition(CanSeek ? _coordinator.TimelineTarget ?? State.PositionSeconds : State.PositionSeconds,
            State.DurationSeconds) : 0;
    public double TimelinePreviewValue => CanSeek
        ? PlaybackInteractionCoordinator.ClampPosition(_coordinator.TimelinePreview ?? State.PositionSeconds, State.DurationSeconds) : 0;
    public double VolumeValue => _coordinator.VolumePreview ?? State.Volume;
    public bool HasTimelinePreview => CanSeek && _coordinator.TimelinePreview is not null;
    public string TimelinePreviewText => HasTimelinePreview ? FormatTime(TimelinePreviewValue) : string.Empty;
    public Visibility TimelinePreviewVisibility => HasTimelinePreview ? Visibility.Visible : Visibility.Collapsed;
    public string FeedbackText => _coordinator.FeedbackText;
    public Visibility FeedbackVisibility => string.IsNullOrEmpty(FeedbackText) || StatusVisibility == Visibility.Visible ||
        State.CurrentOverlay != OverlayKind.None ? Visibility.Collapsed : Visibility.Visible;
    public bool HasActiveInteraction => _coordinator.HasActiveInteraction;

    public bool BeginTimelineScrub(double seconds)
    {
        if (!CanSeek || !_coordinator.BeginTimelineScrub(State, seconds)) return false;
        InvalidateSeek();
        RevealControls();
        NotifyInteractionProperties();
        return true;
    }

    public void UpdateTimelineScrub(double seconds)
    {
        if (CanSeek) _coordinator.UpdateTimelineScrub(State, seconds);
        NotifyInteractionProperties();
    }

    public Task CommitTimelineScrubAsync(CancellationToken cancellationToken = default)
    {
        var request = CanSeek ? _coordinator.CommitTimelineScrub(State) : null;
        NotifyInteractionProperties();
        return ExecuteInteractionAsync(request, cancellationToken);
    }

    public void CancelTimelineScrub() => CancelInputInteractions();

    public void PreviewTimeline(double seconds)
    {
        if (CanSeek) _coordinator.PreviewTimeline(State, seconds);
        NotifyInteractionProperties();
    }

    public void ClearTimelineHover()
    {
        _coordinator.ClearTimelineHover();
        NotifyInteractionProperties();
    }

    public void QueueTimelineSeek(double seconds)
    {
        if (!CanSeek) return;
        _coordinator.QueueTimelineSeek(State, seconds);
        RevealControls();
        NotifyInteractionProperties();
    }

    public void QueueVolumeChange(double volume)
    {
        if (_disposed || HasTerminalFailure || IsStopping) return;
        _coordinator.QueueVolumeChange(State, volume);
        RevealControls();
        NotifyInteractionProperties();
    }

    public bool BeginVideoGesture()
    {
        if (_disposed || HasTerminalFailure || IsStopping || !_coordinator.BeginVideoGesture(State)) return false;
        InvalidateSeek();
        RevealControls();
        NotifyInteractionProperties();
        return true;
    }

    public void UpdateVideoGesture(double totalDeltaX, double totalDeltaY)
    {
        if (!_disposed && !HasTerminalFailure && !IsStopping)
            _coordinator.UpdateVideoGesture(State, totalDeltaX, totalDeltaY);
        NotifyInteractionProperties();
    }

    public Task CommitVideoGestureAsync(CancellationToken cancellationToken = default)
    {
        var request = !_disposed && !HasTerminalFailure && !IsStopping ? _coordinator.CommitVideoGesture(State) : null;
        NotifyInteractionProperties();
        return ExecuteInteractionAsync(request, cancellationToken);
    }

    public void CancelInputInteractions()
    {
        _coordinator.Cancel();
        InvalidateSeek();
        _volumeVersion++;
        var previous = _inputCts;
        _inputCts = new();
        previous.Cancel();
        previous.Dispose();
        NotifyInteractionProperties();
    }

    public async Task ProcessInteractionTickAsync(CancellationToken cancellationToken = default)
    {
        var requests = _coordinator.TakeDueRequests();
        NotifyInteractionProperties();
        // 取出后不再留在待提交队列中，重复 tick 也不能发送同一请求。
        await Task.WhenAll(requests.Select(request => ExecuteInteractionAsync(request, cancellationToken)));
    }

    private Task ExecuteInteractionAsync(InteractionRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !_coordinator.IsCurrent(request) || _disposed || HasTerminalFailure || IsStopping)
            return Task.CompletedTask;
        return request.Kind == PlayerGesture.Seek
            ? ExecuteSeekAsync(request.Value, false, request, cancellationToken)
            : ExecuteVolumeAsync(request.Value, request, cancellationToken);
    }

    private void NotifyInteractionProperties()
    {
        // 释放仍需取消队列与旧回复，但已经关闭的界面不再接收绑定通知。
        if (_disposed) return;
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(TimelineMaximum));
        OnPropertyChanged(nameof(TimelineValue));
        OnPropertyChanged(nameof(TimelinePreviewValue));
        OnPropertyChanged(nameof(VolumeValue));
        OnPropertyChanged(nameof(HasTimelinePreview));
        OnPropertyChanged(nameof(TimelinePreviewText));
        OnPropertyChanged(nameof(TimelinePreviewVisibility));
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(FeedbackVisibility));
        OnPropertyChanged(nameof(HasActiveInteraction));
        NotifyPresentationProperties();
    }
}
