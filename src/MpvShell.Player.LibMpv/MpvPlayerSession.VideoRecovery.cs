using System.Diagnostics;
using System.Globalization;

namespace MpvShell.Player.LibMpv;

public sealed partial class MpvPlayerSession
{
    private readonly SemaphoreSlim _playbackMutationGate = new(1, 1);
    private bool _renderingFailed; // 仅在 _playbackMutationGate 内访问；同一会话不退出终态。

    /// <summary>不可恢复故障同时暂停媒体并封锁后续播放修改，包括已在恢复租约后排队的命令。</summary>
    public async ValueTask PauseForRenderingFailureAsync(CancellationToken cancellationToken)
    {
        await _playbackMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 暂停请求失败也必须保留终态，避免等待者再次开始播放。
            _renderingFailed = true;
            await SetPropertyCoreAsync("pause", true, cancellationToken).ConfigureAwait(false);
        }
        finally { _playbackMutationGate.Release(); }
    }

    private async Task<object?> MutatePlaybackAsync(Func<Task<object?>> action, CancellationToken cancellationToken,
        bool allowAfterRenderingFailure = false)
    {
        await _playbackMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_renderingFailed && !allowAfterRenderingFailure)
                throw new InvalidOperationException("视频渲染已停止，请重新打开播放器。");
            return await action().ConfigureAwait(false);
        }
        finally { _playbackMutationGate.Release(); }
    }

    /// <summary>
    /// 先主动停用视频轨，再允许释放 render context。直接释放活动 context 会使 mpv
    /// 将视频轨标记为失败，只有视频的媒体还会被终止。租约期间暂停普通播放修改，
    /// 调用方必须在创建新 context 后 Resume，或在失败时 Abort；不得在渲染线程等待。
    /// </summary>
    public async ValueTask<VideoRecoveryState> SuspendVideoForRecoveryAsync(CancellationToken cancellationToken)
    {
        await _playbackMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var state = new VideoRecoveryState(this);
        var pauseRequested = false;
        var videoDisableRequested = false;
        try
        {
            state.PlaylistEntryId = await GetPlayingEntryIdAsync(cancellationToken).ConfigureAwait(false);
            var video = await GetOptionalRecoveryPropertyAsync("vid", cancellationToken).ConfigureAwait(false);
            state.VideoTrackId = video switch
            {
                long id when id > 0 && id <= int.MaxValue => (int)id,
                int id when id > 0 => id,
                _ => null,
            };
            if (state.PlaylistEntryId is null || state.VideoTrackId is null) return state;

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
            Trace.WriteLine("[libmpv] 图形重建前已暂停并停用视频轨；保留当前媒体和播放位置。");
            return state;
        }
        catch
        {
            try
            {
                // 尚未要求停用视频时，失败/取消不能把原本播放中的媒体遗留在临时暂停状态。
                // vid=no 一旦提交，取消也无法证明它未执行；此时保留暂停，由故障路径处理。
                if (pauseRequested && !videoDisableRequested &&
                    await GetPlayingEntryIdAsync(CancellationToken.None).ConfigureAwait(false) == state.PlaylistEntryId)
                    await SetPropertyCoreAsync("pause", state.WasPaused, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                Trace.WriteLine($"[libmpv] 取消视频挂起后的暂停状态恢复未完成：{cleanupFailure.Message}");
            }
            finally { ReleaseRecoveryState(state); }
            throw;
        }
    }

    /// <summary>
    /// 调用方已经创建并绑定新 render context；恢复当前媒体的视频轨、可定位位置及暂停状态。
    /// 媒体身份不匹配时拒绝把旧状态应用到新文件。无论成功失败，均归还播放修改租约。
    /// </summary>
    public async ValueTask ResumeVideoAfterRecoveryAsync(VideoRecoveryState state, CancellationToken cancellationToken)
    {
        ValidateRecoveryState(state);
        if (Interlocked.CompareExchange(ref state.Completing, 1, 0) != 0)
            throw new InvalidOperationException("视频恢复租约已经完成。");
        try
        {
            if (!state.VideoSuspended) return;
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
            Trace.WriteLine("[libmpv] 图形重建后已恢复原视频轨、播放位置与暂停状态；未重新加载媒体。");
        }
        catch
        {
            // 恢复未完成时不能遗留活动视频，让后续释放 context 再触发 mpv 的媒体错误。
            try
            {
                if (state.VideoSuspended && await GetPlayingEntryIdAsync(CancellationToken.None).ConfigureAwait(false) == state.PlaylistEntryId)
                {
                    await SetPropertyCoreAsync("pause", true, CancellationToken.None).ConfigureAwait(false);
                    await SetPropertyCoreAsync("vid", "no", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception cleanupFailure)
            {
                Trace.WriteLine($"[libmpv] 视频恢复失败后的停用清理未完成：{cleanupFailure.Message}");
            }
            throw;
        }
        finally { ReleaseRecoveryState(state); }
    }

    /// <summary>
    /// 图形重建失败时归还租约。已停用的视频继续保持停用和暂停，避免在无可用输出时继续播放。
    /// 成功 Resume 后再次调用也安全；调用方可无条件放在 finally 中。
    /// </summary>
    public ValueTask AbortVideoRecoveryAsync(VideoRecoveryState state)
    {
        ValidateRecoveryState(state);
        if (Interlocked.CompareExchange(ref state.Completing, 1, 0) == 0)
            ReleaseRecoveryState(state);
        return ValueTask.CompletedTask;
    }

    private async Task EnsureSameMediaAsync(VideoRecoveryState state, CancellationToken cancellationToken)
    {
        if (await GetPlayingEntryIdAsync(cancellationToken).ConfigureAwait(false) != state.PlaylistEntryId)
            throw new InvalidOperationException("图形恢复期间当前媒体已经更换，无法应用旧视频状态。");
    }

    private async Task<long?> GetPlayingEntryIdAsync(CancellationToken cancellationToken)
    {
        if (await GetPropertyAsync("playlist", cancellationToken).ConfigureAwait(false) is not object?[] entries)
            return null;
        foreach (var entry in entries.OfType<Dictionary<string, object?>>())
        {
            if (entry.GetValueOrDefault("playing") is true && entry.GetValueOrDefault("id") is long id)
                return id;
        }
        return null;
    }

    private async Task<object?> GetOptionalRecoveryPropertyAsync(string name, CancellationToken cancellationToken)
    {
        try { return await GetPropertyAsync(name, cancellationToken).ConfigureAwait(false); }
        catch (MpvException ex) when (ex.ErrorCode == -10) { return null; } // MPV_ERROR_PROPERTY_UNAVAILABLE
    }

    private void ValidateRecoveryState(VideoRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!ReferenceEquals(state.Owner, this))
            throw new ArgumentException("视频恢复租约属于另一播放器会话。", nameof(state));
    }

    private void ReleaseRecoveryState(VideoRecoveryState state)
    {
        if (Interlocked.Exchange(ref state.Released, 1) == 0)
            _playbackMutationGate.Release();
    }

    /// <summary>一次图形重建持有的播放修改租约；由会话创建并消费，不暴露原生资源。</summary>
    public sealed class VideoRecoveryState
    {
        internal VideoRecoveryState(MpvPlayerSession owner) => Owner = owner;
        internal MpvPlayerSession Owner { get; }
        internal long? PlaylistEntryId { get; set; }
        internal int? VideoTrackId { get; set; }
        internal bool WasPaused { get; set; }
        internal bool IsSeekable { get; set; }
        internal double? PositionSeconds { get; set; }
        internal bool VideoSuspended { get; set; }
        internal int Completing;
        internal int Released;
    }
}
