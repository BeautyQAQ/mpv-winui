using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Services;

public sealed class PlaybackInteractionCoordinator
{
    private readonly TimeProvider _timeProvider;
    private readonly GestureDecisionEngine _gestures = new();
    private long _epoch;
    private long _requestVersion;
    private bool _isScrubbing;
    private bool _isGesturing;
    private PlayerGesture _gesture;
    private double _gesturePosition;
    private double _gestureVolume;
    private double? _scrubTarget;
    private double? _hoverTarget;
    private double? _gestureVolumeTarget;
    private ScheduledInteraction? _pendingSeek;
    private ScheduledInteraction? _pendingVolume;
    private DateTimeOffset? _feedbackExpires;

    public PlaybackInteractionCoordinator(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public double? TimelinePreview => _scrubTarget ?? _pendingSeek?.Request.Value ?? _hoverTarget;
    public double? TimelineTarget => _scrubTarget ?? _pendingSeek?.Request.Value;
    public double? VolumePreview => _gestureVolumeTarget ?? _pendingVolume?.Request.Value;
    public string FeedbackText { get; private set; } = string.Empty;
    public bool HasActiveInteraction => _isScrubbing || _isGesturing || _pendingSeek is not null || _pendingVolume is not null;

    public static bool CanSeek(PlaybackState state) => !state.IsStopped && state.IsSeekable &&
        double.IsFinite(state.DurationSeconds) && state.DurationSeconds > 0;

    public static double ClampPosition(double value, double duration) =>
        double.IsFinite(value) && double.IsFinite(duration) && duration > 0 ? Math.Clamp(value, 0, duration) : 0;

    public PlayerGesture ClassifyGesture(PlaybackState state, double deltaX, double deltaY) =>
        state.CurrentOverlay == OverlayKind.None && !state.IsStopped && double.IsFinite(deltaX) && double.IsFinite(deltaY)
            ? _gestures.Classify(deltaX, deltaY) : PlayerGesture.None;

    public InteractionRequest? CreateCompletedGestureRequest(PlaybackState state, double deltaX, double deltaY, double positionOrigin)
    {
        return ClassifyGesture(state, deltaX, deltaY) switch
        {
            PlayerGesture.Seek when CanSeek(state) => CreateRequest(PlayerGesture.Seek, GesturePosition(state, positionOrigin, deltaX)),
            PlayerGesture.Volume => CreateRequest(PlayerGesture.Volume, GestureVolume(state.Volume, deltaY)),
            _ => null,
        };
    }

    public void PreviewTimeline(PlaybackState state, double seconds)
    {
        if (CanSeek(state) && state.CurrentOverlay == OverlayKind.None && double.IsFinite(seconds))
            _hoverTarget = ClampPosition(seconds, state.DurationSeconds);
    }

    public void ClearTimelineHover() => _hoverTarget = null;

    public bool BeginTimelineScrub(PlaybackState state, double seconds)
    {
        if (!CanSeek(state) || state.CurrentOverlay != OverlayKind.None || !double.IsFinite(seconds)) return false;
        Cancel();
        _isScrubbing = true;
        _scrubTarget = ClampPosition(seconds, state.DurationSeconds);
        return true;
    }

    public void UpdateTimelineScrub(PlaybackState state, double seconds)
    {
        if (_isScrubbing && CanSeek(state) && double.IsFinite(seconds))
            _scrubTarget = ClampPosition(seconds, state.DurationSeconds);
    }

    public InteractionRequest? CommitTimelineScrub(PlaybackState state)
    {
        var target = _isScrubbing && CanSeek(state) ? _scrubTarget : null;
        _isScrubbing = false;
        _scrubTarget = null;
        _hoverTarget = null;
        return target is { } value ? CreateRequest(PlayerGesture.Seek, ClampPosition(value, state.DurationSeconds)) : null;
    }

    public void QueueTimelineSeek(PlaybackState state, double seconds)
    {
        if (!CanSeek(state) || state.CurrentOverlay != OverlayKind.None || !double.IsFinite(seconds) || _isScrubbing || _isGesturing) return;
        _hoverTarget = null;
        _pendingSeek = new(CreateRequest(PlayerGesture.Seek, ClampPosition(seconds, state.DurationSeconds)),
            _timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(180));
    }

    public void QueueVolumeChange(PlaybackState state, double volume)
    {
        if (!double.IsFinite(volume) || state.CurrentOverlay != OverlayKind.None || _isScrubbing || _isGesturing) return;
        _pendingVolume = new(CreateRequest(PlayerGesture.Volume, Math.Clamp(volume, 0, 100)),
            _timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(100));
    }

    public bool BeginVideoGesture(PlaybackState state)
    {
        if (state.CurrentOverlay != OverlayKind.None || state.IsStopped) return false;
        Cancel();
        _isGesturing = true;
        _gesturePosition = state.PositionSeconds;
        _gestureVolume = state.Volume;
        return true;
    }

    public void UpdateVideoGesture(PlaybackState state, double totalDeltaX, double totalDeltaY)
    {
        if (!_isGesturing || !double.IsFinite(totalDeltaX) || !double.IsFinite(totalDeltaY)) return;
        // 一旦超过阈值选定轴，后续斜向移动也不能切换成另一种操作。
        if (_gesture == PlayerGesture.None) _gesture = _gestures.Classify(totalDeltaX, totalDeltaY);
        if (_gesture == PlayerGesture.Seek && CanSeek(state))
            _scrubTarget = GesturePosition(state, _gesturePosition, totalDeltaX);
        else if (_gesture == PlayerGesture.Volume)
        {
            _gestureVolumeTarget = GestureVolume(_gestureVolume, totalDeltaY);
            FeedbackText = $"音量预览 {(int)_gestureVolumeTarget.Value}%";
        }
    }

    public InteractionRequest? CommitVideoGesture(PlaybackState state)
    {
        InteractionRequest? request = null;
        if (_isGesturing && _gesture == PlayerGesture.Seek && CanSeek(state) && _scrubTarget is { } position)
            request = CreateRequest(PlayerGesture.Seek, ClampPosition(position, state.DurationSeconds));
        else if (_isGesturing && _gesture == PlayerGesture.Volume && _gestureVolumeTarget is { } volume)
            request = CreateRequest(PlayerGesture.Volume, volume);
        _isGesturing = false;
        _gesture = PlayerGesture.None;
        _scrubTarget = null;
        _gestureVolumeTarget = null;
        if (request is null) FeedbackText = string.Empty;
        return request;
    }

    public IReadOnlyList<InteractionRequest> TakeDueRequests()
    {
        Tick();
        var now = _timeProvider.GetUtcNow();
        var requests = new List<InteractionRequest>(2);
        if (_pendingSeek is { } seek && now >= seek.Due)
        {
            _pendingSeek = null;
            requests.Add(seek.Request);
        }
        if (_pendingVolume is { } volume && now >= volume.Due)
        {
            _pendingVolume = null;
            requests.Add(volume.Request);
        }
        return requests;
    }

    public InteractionRequest CreateRequest(PlayerGesture kind, double value)
    {
        FeedbackText = string.Empty;
        _feedbackExpires = null;
        return new(kind, value, _epoch, ++_requestVersion);
    }

    public bool IsCurrent(InteractionRequest request) => request.Epoch == _epoch;

    public void MarkSubmitted(InteractionRequest request)
    {
        if (IsLatest(request)) FeedbackText = request.Kind == PlayerGesture.Seek ? "正在跳转…" : "正在调整音量…";
    }

    public void Complete(InteractionRequest request, string message)
    {
        if (!IsLatest(request)) return;
        FeedbackText = message;
        _feedbackExpires = _timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(1500);
    }

    public void Tick()
    {
        if (_feedbackExpires is { } expires && _timeProvider.GetUtcNow() >= expires)
        {
            FeedbackText = string.Empty;
            _feedbackExpires = null;
        }
    }

    public void Cancel()
    {
        _epoch++;
        _isScrubbing = _isGesturing = false;
        _gesture = PlayerGesture.None;
        _scrubTarget = _hoverTarget = _gestureVolumeTarget = null;
        _pendingSeek = _pendingVolume = null;
        _feedbackExpires = null;
        FeedbackText = string.Empty;
    }

    private bool IsLatest(InteractionRequest request) => IsCurrent(request) && request.Version == _requestVersion;
    private static double GesturePosition(PlaybackState state, double origin, double deltaX) =>
        ClampPosition(origin + deltaX / 8, state.DurationSeconds);
    private static double GestureVolume(double origin, double deltaY) => Math.Clamp(origin - deltaY / 4, 0, 100);
    private sealed record ScheduledInteraction(InteractionRequest Request, DateTimeOffset Due);

    public PlaybackState ShowControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = true,
            CurrentOverlay = OverlayKind.None,
        };

    public PlaybackState HideControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = false,
            CurrentOverlay = OverlayKind.None,
        };

    public PlaybackState ToggleOverlay(PlaybackState state, OverlayKind overlay) =>
        state.CurrentOverlay == overlay
            ? state with { CurrentOverlay = OverlayKind.None }
            : state with { CurrentOverlay = overlay, AreControlsVisible = true };

    public PlaybackState OnIdleTimeout(PlaybackState state) =>
        state.CurrentOverlay == OverlayKind.None
            ? state with { AreControlsVisible = false }
            : state;
}

public sealed record InteractionRequest(PlayerGesture Kind, double Value, long Epoch, long Version);
