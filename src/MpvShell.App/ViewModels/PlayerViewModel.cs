using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlayerBackend _backend;
    private readonly PlaybackInteractionCoordinator _coordinator;
    private readonly GestureDecisionEngine _gestureDecisionEngine;
    private bool _isInitialized;
    private PlaybackState _state = PlaybackState.Initial;
    private string _urlText = string.Empty;

    public PlayerViewModel(IPlayerBackend backend, PlaybackInteractionCoordinator coordinator)
        : this(backend, coordinator, new GestureDecisionEngine())
    {
    }

    public PlayerViewModel(
        IPlayerBackend backend,
        PlaybackInteractionCoordinator coordinator,
        GestureDecisionEngine gestureDecisionEngine)
    {
        _backend = backend;
        _coordinator = coordinator;
        _gestureDecisionEngine = gestureDecisionEngine;
    }

    public PlaybackState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string UrlText
    {
        get => _urlText;
        set => SetProperty(ref _urlText, value);
    }

    public async Task InitializeAsync(nint hostHandle)
    {
        if (_isInitialized || hostHandle == 0)
        {
            return;
        }

        await _backend.InitializeAsync(hostHandle, CancellationToken.None);
        _isInitialized = true;
    }

    public void OnIdleTimeout()
    {
        State = _coordinator.OnIdleTimeout(State);
    }

    public async Task HandleDragAsync(double deltaX, double deltaY)
    {
        if (_gestureDecisionEngine.Classify(deltaX, deltaY) != PlayerGesture.Seek)
        {
            return;
        }

        var deltaSeconds = deltaX / 8.0;

        if (Math.Abs(deltaSeconds) < 1)
        {
            return;
        }

        await SeekRelativeAsync(deltaSeconds);
    }

    public async Task SeekToAsync(double seconds)
    {
        var clampedSeconds = ClampPosition(seconds);
        await _backend.SetPositionAsync(clampedSeconds, CancellationToken.None);
        State = _coordinator.ShowControls(State with { PositionSeconds = clampedSeconds });
    }

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        var url = UrlText.Trim();
        await _backend.LoadUrlAsync(url, CancellationToken.None);
        State = _coordinator.ShowControls(State with { CurrentUrl = url });
    }

    [RelayCommand]
    private void ShowControls()
    {
        State = _coordinator.ShowControls(State);
    }

    [RelayCommand]
    private void HideControls()
    {
        State = _coordinator.HideControls(State);
    }

    [RelayCommand]
    private void ToggleOsd()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.Osd);
    }

    [RelayCommand]
    private void ToggleTracks()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.Tracks);
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (State.IsPlaying)
        {
            await _backend.PauseAsync(CancellationToken.None);
            State = _coordinator.ShowControls(State with { IsPlaying = false });
            return;
        }

        await _backend.PlayAsync(CancellationToken.None);
        State = _coordinator.ShowControls(State with { IsPlaying = true });
    }

    [RelayCommand]
    private Task SeekBackwardAsync() => SeekRelativeAsync(-15);

    [RelayCommand]
    private Task SeekForwardAsync() => SeekRelativeAsync(30);

    private async Task SeekRelativeAsync(double deltaSeconds)
    {
        await _backend.SeekAsync(deltaSeconds, CancellationToken.None);
        var nextPosition = ClampPosition(State.PositionSeconds + deltaSeconds);
        State = _coordinator.ShowControls(State with { PositionSeconds = nextPosition });
    }

    private double ClampPosition(double seconds) =>
        State.DurationSeconds > 0
            ? Math.Clamp(seconds, 0, State.DurationSeconds)
            : Math.Max(0, seconds);
}
