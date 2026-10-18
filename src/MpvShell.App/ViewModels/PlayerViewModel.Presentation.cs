using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MpvShell.Player.Abstractions.Models;
using System.Collections.ObjectModel;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel
{
    private bool _isLoading;
    private bool _hasReachedEnd;
    private bool _replacingTracks;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            NotifyInteractionProperties();
        }
    }

    // 单一状态卡保证错误、加载、缓冲与结束提示不会彼此遮挡。
    private PlayerStatus CurrentStatus => !string.IsNullOrWhiteSpace(ErrorMessage) ? PlayerStatus.Error
        : IsStopping ? PlayerStatus.Stopping
        : IsLoading ? PlayerStatus.Loading
        : _hasReachedEnd ? PlayerStatus.Ended
        : IsBuffering ? PlayerStatus.Buffering
        : State.IsStopped && !string.IsNullOrWhiteSpace(State.CurrentUrl) ? PlayerStatus.Stopped
        : PlayerStatus.None;

    public Visibility StatusVisibility => State.CurrentOverlay == OverlayKind.None && CurrentStatus != PlayerStatus.None
        ? Visibility.Visible : Visibility.Collapsed;
    public bool IsStatusBusy => StatusVisibility == Visibility.Visible && CurrentStatus is PlayerStatus.Loading or PlayerStatus.Buffering or PlayerStatus.Stopping;
    public Visibility StatusProgressVisibility => IsStatusBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WelcomeVisibility => State.CurrentOverlay == OverlayKind.None && string.IsNullOrWhiteSpace(State.CurrentUrl) && CurrentStatus == PlayerStatus.None
        ? Visibility.Visible : Visibility.Collapsed;
    public string StatusTitle => CurrentStatus switch
    {
        PlayerStatus.Error => HasTerminalFailure ? "播放器暂不可用" : "播放遇到问题",
        PlayerStatus.Loading => "正在打开媒体",
        PlayerStatus.Buffering => "正在缓冲",
        PlayerStatus.Stopping => "正在停止",
        PlayerStatus.Ended => "播放结束",
        PlayerStatus.Stopped => "已停止",
        _ => string.Empty,
    };
    public string StatusMessage => CurrentStatus switch
    {
        PlayerStatus.Error => ErrorMessage ?? string.Empty,
        PlayerStatus.Loading => "正在读取媒体信息，可使用停止取消本次打开。",
        PlayerStatus.Buffering => "正在等待媒体数据，请稍候。",
        PlayerStatus.Stopping => "正在释放当前媒体，请稍候。",
        PlayerStatus.Ended => "点击播放可从头重新播放，也可以打开其他媒体。",
        PlayerStatus.Stopped => "点击播放可重新打开当前媒体。",
        _ => string.Empty,
    };

    public bool CanTogglePlayback => !_disposed && !HasTerminalFailure && !IsStopping && !IsLoading && !string.IsNullOrWhiteSpace(State.CurrentUrl);
    public bool CanStopPlayback => !_disposed && !HasTerminalFailure && !IsStopping && (IsLoading || !State.IsStopped);
    public bool CanChangeSpeed => !_disposed && !HasTerminalFailure && !IsStopping;
    public bool CanAdjustVolume => !_disposed && !HasTerminalFailure && !IsStopping;
    public bool CanSelectTracks => !_disposed && !HasTerminalFailure && !IsStopping && !IsLoading && !State.IsStopped && Tracks.Count > 0;
    public string PlaybackDisabledReason => CanTogglePlayback ? string.Empty : UnavailableReason ?? "请先打开媒体。";
    public string StopDisabledReason => CanStopPlayback ? string.Empty : UnavailableReason ?? "当前没有正在播放的媒体。";
    public string SpeedDisabledReason => CanChangeSpeed ? string.Empty : UnavailableReason ?? string.Empty;
    public string VolumeDisabledReason => CanAdjustVolume ? string.Empty : UnavailableReason ?? string.Empty;
    public string TracksDisabledReason => CanSelectTracks ? string.Empty : UnavailableReason
        ?? (State.IsStopped ? "请先播放媒体，再选择音轨或字幕。" : "此媒体没有可选择的音轨或字幕。");
    public string SeekDisabledReason => CanSeek ? string.Empty : UnavailableReason
        ?? (State.IsStopped ? "请先播放媒体，再调整进度。"
            : !State.IsSeekable ? "当前媒体不支持调整进度。" : "媒体时长未知，暂时无法调整进度。");
    public string VolumeLabel => $"{VolumeValue:0}%";

    private string? UnavailableReason => HasTerminalFailure ? "播放器发生不可恢复的错误，请重新启动。"
        : IsStopping ? "正在停止，请稍候。"
        : IsLoading ? "媒体正在打开，请稍候。" : null;

    public ObservableCollection<TrackOption> AudioTrackOptions { get; } = new();
    public ObservableCollection<TrackOption> SubtitleTrackOptions { get; } = new();
    public Visibility AudioTracksEmptyVisibility => AudioTrackOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubtitleTracksEmptyVisibility => SubtitleTrackOptions.Count <= 1 ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private Task SelectTrackOptionAsync(TrackOption? option) => SelectTrackAsync(option?.Track);

    private void RefreshTrackOptions()
    {
        if (_replacingTracks) return;
        var subtitles = Tracks.Where(IsSubtitleTrack).Where(track => track.Id > 0).ToArray();
        UpdateTrackOptions(AudioTrackOptions, Tracks.Where(IsAudioTrack).ToArray());
        UpdateTrackOptions(SubtitleTrackOptions,
            [new(0, "sub", null, "关闭字幕", !subtitles.Any(track => track.Selected)), .. subtitles]);
        OnPropertyChanged(nameof(AudioTracksEmptyVisibility));
        OnPropertyChanged(nameof(SubtitleTracksEmptyVisibility));
        OnPropertyChanged(nameof(CanSelectTracks));
        OnPropertyChanged(nameof(TracksDisabledReason));
    }

    private static void UpdateTrackOptions(ObservableCollection<TrackOption> options, IReadOnlyList<TrackInfo> tracks)
    {
        // 选中状态的后端回传不重建列表容器，键盘/触控焦点留在用户操作的轨道上。
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var existing = -1;
            for (var candidate = index; candidate < options.Count; candidate++)
            {
                if (options[candidate].Track.Id == track.Id)
                {
                    existing = candidate;
                    break;
                }
            }
            if (existing < 0) options.Insert(index, new(track));
            else
            {
                if (existing != index) options.Move(existing, index);
                options[index].Update(track);
            }
        }
        while (options.Count > tracks.Count) options.RemoveAt(options.Count - 1);
    }

    private static bool IsSubtitleTrack(TrackInfo track) => track.Kind.Equals("sub", StringComparison.OrdinalIgnoreCase);

    private void NotifyPresentationProperties()
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(StatusVisibility));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(IsStatusBusy));
        OnPropertyChanged(nameof(StatusProgressVisibility));
        OnPropertyChanged(nameof(WelcomeVisibility));
        OnPropertyChanged(nameof(FeedbackVisibility));
        OnPropertyChanged(nameof(CanTogglePlayback));
        OnPropertyChanged(nameof(CanStopPlayback));
        OnPropertyChanged(nameof(CanChangeSpeed));
        OnPropertyChanged(nameof(CanAdjustVolume));
        OnPropertyChanged(nameof(CanSelectTracks));
        OnPropertyChanged(nameof(PlaybackDisabledReason));
        OnPropertyChanged(nameof(StopDisabledReason));
        OnPropertyChanged(nameof(SpeedDisabledReason));
        OnPropertyChanged(nameof(VolumeDisabledReason));
        OnPropertyChanged(nameof(TracksDisabledReason));
        OnPropertyChanged(nameof(SeekDisabledReason));
        OnPropertyChanged(nameof(VolumeLabel));
    }

    private enum PlayerStatus { None, Error, Loading, Buffering, Stopping, Ended, Stopped }
}
