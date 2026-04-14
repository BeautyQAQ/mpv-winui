using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;
using System.Collections.ObjectModel;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlayerBackend _backend;
    private readonly PlaybackInteractionCoordinator _coordinator;
    private readonly GestureDecisionEngine _gestureDecisionEngine;
    private readonly RecentUrlStore _recentUrlStore;
    private bool _isInitialized;
    private CancellationTokenSource? _eventPumpCts;
    private Task? _eventPumpTask;
    private PlaybackState _state = PlaybackState.Initial;
    private string _urlText = string.Empty;

    public PlayerViewModel(IPlayerBackend backend, PlaybackInteractionCoordinator coordinator)
        : this(backend, coordinator, new GestureDecisionEngine(), new RecentUrlStore(), new InfoPanelViewModel())
    {
    }

    public PlayerViewModel(
        IPlayerBackend backend,
        PlaybackInteractionCoordinator coordinator,
        GestureDecisionEngine gestureDecisionEngine)
        : this(backend, coordinator, gestureDecisionEngine, new RecentUrlStore(), new InfoPanelViewModel())
    {
    }

    public PlayerViewModel(
        IPlayerBackend backend,
        PlaybackInteractionCoordinator coordinator,
        GestureDecisionEngine gestureDecisionEngine,
        RecentUrlStore recentUrlStore,
        InfoPanelViewModel infoPanel)
    {
        _backend = backend;
        _coordinator = coordinator;
        _gestureDecisionEngine = gestureDecisionEngine;
        _recentUrlStore = recentUrlStore;
        InfoPanel = infoPanel;
    }

    public InfoPanelViewModel InfoPanel { get; }

    public ObservableCollection<TrackInfo> Tracks { get; } = new();

    public ObservableCollection<string> RecentUrls { get; } = new();

    public Visibility ControlsVisibility =>
        State.AreControlsVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfoPanelVisibility =>
        State.CurrentOverlay == OverlayKind.InfoPanel ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OsdVisibility =>
        State.CurrentOverlay == OverlayKind.Osd ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TracksVisibility =>
        State.CurrentOverlay == OverlayKind.Tracks ? Visibility.Visible : Visibility.Collapsed;

    public PlaybackState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ControlsVisibility));
            OnPropertyChanged(nameof(InfoPanelVisibility));
            OnPropertyChanged(nameof(OsdVisibility));
            OnPropertyChanged(nameof(TracksVisibility));
        }
    }

    public string UrlText
    {
        get => _urlText;
        set => SetProperty(ref _urlText, value);
    }

    private string? _errorMessage;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ErrorVisibility));
        }
    }

    public Visibility ErrorVisibility =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public async Task InitializeAsync(nint hostHandle)
    {
        if (_isInitialized || hostHandle == 0)
        {
            return;
        }

        try
        {
            await _backend.InitializeAsync(hostHandle, CancellationToken.None);
            _isInitialized = true;
            StartEventPump();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
        try
        {
            var clampedSeconds = ClampPosition(seconds);
            await _backend.SetPositionAsync(clampedSeconds, CancellationToken.None);
            State = _coordinator.ShowControls(State with { PositionSeconds = clampedSeconds });
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        try
        {
            await LoadUrlAsync(UrlText.Trim());
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
    private void ToggleInfoPanel()
    {
        State = _coordinator.ToggleOverlay(State, OverlayKind.InfoPanel);
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        try
        {
            if (State.IsPlaying)
            {
                await _backend.PauseAsync(CancellationToken.None);
                State = _coordinator.ShowControls(State with { IsPlaying = false });
                ErrorMessage = null;
                return;
            }

            await _backend.PlayAsync(CancellationToken.None);
            State = _coordinator.ShowControls(State with { IsPlaying = true });
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task SeekBackwardAsync() => SeekRelativeAsync(-15);

    [RelayCommand]
    private Task SeekForwardAsync() => SeekRelativeAsync(30);

    [RelayCommand]
    private async Task SelectTrackAsync(TrackInfo? track)
    {
        if (track is null)
        {
            return;
        }

        try
        {
            if (IsAudioTrack(track))
            {
                await _backend.SetAudioTrackAsync(track.Id, CancellationToken.None);
            }
            else
            {
                await _backend.SetSubtitleTrackAsync(track.Id, CancellationToken.None);
            }

            ReplaceTrackSelection(track);
            State = _coordinator.ShowControls(State);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task SeekRelativeAsync(double deltaSeconds)
    {
        try
        {
            await _backend.SeekAsync(deltaSeconds, CancellationToken.None);
            var nextPosition = ClampPosition(State.PositionSeconds + deltaSeconds);
            State = _coordinator.ShowControls(State with { PositionSeconds = nextPosition });
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadUrlAsync(string url)
    {
        await _backend.LoadUrlAsync(url, CancellationToken.None);

        _recentUrlStore.Add(url);
        RefreshRecentUrls();

        InfoPanel.Update(await _backend.GetInfoSnapshotAsync(CancellationToken.None));
        ReplaceTracks(await _backend.GetTracksAsync(CancellationToken.None));

        UrlText = url;
        State = _coordinator.ShowControls(State with { CurrentUrl = url });
    }

    private void RefreshRecentUrls()
    {
        RecentUrls.Clear();

        foreach (var recentUrl in _recentUrlStore.Items)
        {
            RecentUrls.Add(recentUrl);
        }
    }

    private void ReplaceTracks(IReadOnlyList<TrackInfo> tracks)
    {
        Tracks.Clear();

        foreach (var track in tracks)
        {
            Tracks.Add(track);
        }
    }

    private void ReplaceTrackSelection(TrackInfo selectedTrack)
    {
        for (var i = 0; i < Tracks.Count; i++)
        {
            var track = Tracks[i];
            var sameFamily = string.Equals(track.Kind, selectedTrack.Kind, StringComparison.OrdinalIgnoreCase);
            Tracks[i] = sameFamily ? track with { Selected = track.Id == selectedTrack.Id } : track;
        }
    }

    private static bool IsAudioTrack(TrackInfo track) =>
        string.Equals(track.Kind, "audio", StringComparison.OrdinalIgnoreCase);

    private double ClampPosition(double seconds) =>
        State.DurationSeconds > 0
            ? Math.Clamp(seconds, 0, State.DurationSeconds)
            : Math.Max(0, seconds);

    private void StartEventPump()
    {
        _eventPumpCts?.Cancel();
        _eventPumpCts?.Dispose();

        _eventPumpCts = new CancellationTokenSource();
        _eventPumpTask = Task.Run(() => ObserveBackendEventsAsync(_eventPumpCts.Token));
    }

    private async Task ObserveBackendEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var playerEvent in _backend.ObserveEventsAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                switch (playerEvent)
                {
                    case PlaybackStateChanged stateChanged:
                        State = stateChanged.State;
                        break;
                    case TracksChanged tracksChanged:
                        ReplaceTracks(tracksChanged.Tracks);
                        break;
                    case BackendFaulted faulted:
                        ErrorMessage = faulted.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
