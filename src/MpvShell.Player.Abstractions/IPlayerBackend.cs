using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions;

public interface IPlayerBackend : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    /// <summary>
    /// 等待媒体完成加载；取消或超时只结束调用方等待，不撤回已提交的原生加载。
    /// 当前地址从原生接受加载时切换，实际播放状态由事件流同步。
    /// </summary>
    Task LoadUrlAsync(string url, CancellationToken cancellationToken);
    Task PlayAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    /// <summary>提交相对跳转；命令完成不保证解码已到目标位置，实际进度以状态事件为准。</summary>
    Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken);
    /// <summary>提交绝对跳转；取消等待不等于撤销已提交的跳转。</summary>
    Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken);
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken);
    Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken);
    Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken);
    Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken);
    /// <summary>供单个连续消费者同步状态；高频属性可合并，不能用作无损事件历史。</summary>
    IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(CancellationToken cancellationToken);
}
