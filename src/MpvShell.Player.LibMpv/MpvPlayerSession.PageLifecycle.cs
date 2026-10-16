using System.Diagnostics;
using System.Globalization;

namespace MpvShell.Player.LibMpv;

public sealed partial class MpvPlayerSession
{
    private VideoRecoveryState? _pendingDetach;

    /// <summary>
    /// 页面/渲染器卸载前挂起视频，再允许释放 render context。直接释放活动 context 会使 mpv
    /// 把视频轨标记为失败，甚至终止只有视频的媒体，因此这里复用图形恢复的暂停+停用语义；
    /// 不同之处是不把租约交给调用方，而是把待恢复状态寄存到会话内，由重新挂载后的
    /// <see cref="ResumeVideoAfterDetachAsync"/> 消费。无媒体、无视频轨或终态时为 no-op；
    /// 挂起期间普通播放修改进入等待，直到恢复归还租约或会话销毁。
    /// </summary>
    public async ValueTask SuspendVideoForDetachAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_pendingDetach is not null) return; // 已有待恢复状态：保持第一次的记录，租约仍由它持有。
        }
        await _playbackMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var state = new VideoRecoveryState(this);
        var pauseRequested = false;
        var videoDisableRequested = false;
        try
        {
            if (_renderingFailed) return; // 终态由故障路径管理；finally 归还租约。
            state.PlaylistEntryId = await GetPlayingEntryIdAsync(cancellationToken).ConfigureAwait(false);
            var video = await GetOptionalRecoveryPropertyAsync("vid", cancellationToken).ConfigureAwait(false);
            state.VideoTrackId = video switch
            {
                long id when id > 0 && id <= int.MaxValue => (int)id,
                int id when id > 0 => id,
                _ => null,
            };
            if (state.PlaylistEntryId is null || state.VideoTrackId is null) return;

            state.WasPaused = await GetPropertyAsync("pause", cancellationToken).ConfigureAwait(false) is true;
            state.IsSeekable = await GetOptionalRecoveryPropertyAsync("seekable", cancellationToken).ConfigureAwait(false) is true;
            await EnsureSameMediaAsync(state, cancellationToken).ConfigureAwait(false);
            pauseRequested = true;
            await SetPropertyCoreAsync("pause", true, cancellationToken).ConfigureAwait(false);
            var position = await GetOptionalRecoveryPropertyAsync("time-pos", cancellationToken).ConfigureAwait(false);
            state.PositionSeconds = position switch
            {
                double seconds when double.IsFinite(seconds) && seconds >= 0 => seconds,
                long seconds when seconds >= 0 => seconds,
                _ => null,
            };
            await EnsureSameMediaAsync(state, cancellationToken).ConfigureAwait(false);
            videoDisableRequested = true;
            await SetPropertyCoreAsync("vid", "no", cancellationToken).ConfigureAwait(false);
            state.VideoSuspended = true;
            lock (_lifecycle) _pendingDetach = state;
            Trace.WriteLine("[libmpv] 视频已挂起等待重新挂载；播放修改租约保留到恢复或会话销毁。");
        }
        catch
        {
            try
            {
                // 与恢复租约相同：尚未要求停用视频时，失败/取消不能把播放中的媒体留在临时暂停状态。
                if (pauseRequested && !videoDisableRequested &&
                    await GetPlayingEntryIdAsync(CancellationToken.None).ConfigureAwait(false) == state.PlaylistEntryId)
                    await SetPropertyCoreAsync("pause", state.WasPaused, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                Trace.WriteLine($"[libmpv] 视频挂起失败后的暂停恢复未完成：{cleanupFailure.Message}");
            }
            throw;
        }
        finally
        {
            // 寄存成功的状态持有租约；其余路径（含异常）立即归还。
            if (!state.VideoSuspended) ReleaseRecoveryState(state);
        }
    }

    /// <summary>
    /// 页面重新挂载后恢复被挂起的视频轨、定位与暂停状态，并归还挂起期间持有的播放修改租约。
    /// 尽力而为：媒体已经更换或命令失败时保持视频停用并放弃旧状态，不升级为终态；没有待恢复状态时为 no-op。
    /// </summary>
    public async ValueTask ResumeVideoAfterDetachAsync(CancellationToken cancellationToken)
    {
        VideoRecoveryState? state;
        lock (_lifecycle)
        {
            state = _pendingDetach;
            _pendingDetach = null;
        }
        if (state is null) return;
        try
        {
            if (_renderingFailed) return; // 终态封锁播放修改：保持停用，最终由故障路径释放。
            await EnsureSameMediaAsync(state, cancellationToken).ConfigureAwait(false);
            await SetPropertyCoreAsync("vid", state.VideoTrackId!.Value, cancellationToken).ConfigureAwait(false);
            if (state.IsSeekable && state.PositionSeconds is { } seconds)
            {
                await CommandCoreAsync(["seek", seconds.ToString("R", CultureInfo.InvariantCulture), "absolute+exact"],
                    cancellationToken).ConfigureAwait(false);
            }
            await EnsureSameMediaAsync(state, cancellationToken).ConfigureAwait(false);
            await SetPropertyCoreAsync("pause", state.WasPaused, cancellationToken).ConfigureAwait(false);
            state.VideoSuspended = false;
            Trace.WriteLine("[libmpv] 重新挂载后已恢复原视频轨、播放位置与暂停状态。");
        }
        catch (Exception ex)
        {
            // 恢复未完成时不能遗留活动视频；与图形恢复一致，保持停用和暂停，等下一次交接或关闭。
            try
            {
                if (state.VideoSuspended &&
                    await GetPlayingEntryIdAsync(CancellationToken.None).ConfigureAwait(false) == state.PlaylistEntryId)
                {
                    await SetPropertyCoreAsync("pause", true, CancellationToken.None).ConfigureAwait(false);
                    await SetPropertyCoreAsync("vid", "no", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception cleanupFailure)
            {
                Trace.WriteLine($"[libmpv] 视频重新挂载恢复失败后的停用清理未完成：{cleanupFailure.Message}");
            }
            Trace.WriteLine($"[libmpv] 视频重新挂载恢复未完成：{ex.Message}");
        }
        finally { ReleaseRecoveryState(state); }
    }
}
