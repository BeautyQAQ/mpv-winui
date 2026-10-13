using MpvShell.Player.Abstractions.Events;

namespace MpvShell.App.Services;

/// <summary>限制 UI 派发积压；未消费的事件继续留在后端有界队列中。</summary>
internal static class UiPlayerEventPump
{
    private const int MaximumBatchSize = 64;

    internal static async Task RunAsync(
        IAsyncEnumerable<PlayerEvent> events,
        Action<Action> dispatch,
        Action<PlayerEvent> apply,
        CancellationToken cancellationToken)
    {
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reader = events.GetAsyncEnumerator(readCancellation.Token);
        Task<bool>? pendingRead = null;
        var batch = new List<PlayerEvent>();
        var segmentStart = 0;
        var readCount = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pendingRead = reader.MoveNextAsync().AsTask();
                // 没有更多同步就绪的事件时立即提交，不等待下一个属性变化才刷新 UI。
                // 已失败的读取也先提交前面的事件，随后才传播事件流故障。
                if (readCount > 0 && !pendingRead.IsCompletedSuccessfully)
                    await FlushAsync().ConfigureAwait(false);

                var hasNext = await pendingRead.ConfigureAwait(false);
                pendingRead = null;
                if (!hasNext)
                {
                    if (readCount > 0) await FlushAsync().ConfigureAwait(false);
                    return;
                }

                var playerEvent = reader.Current;
                if (playerEvent is PlaybackStateChanged or TracksChanged or MediaInfoChanged or BufferingChanged)
                {
                    // 同一连续状态段只保留每类最新快照，并按最后出现的位置应用。
                    // EOF、错误和未来新增的离散事件都是顺序边界，不能被合并掉。
                    for (var index = batch.Count - 1; index >= segmentStart; index--)
                    {
                        if (batch[index].GetType() == playerEvent.GetType())
                        {
                            batch.RemoveAt(index);
                            break;
                        }
                    }
                    batch.Add(playerEvent);
                }
                else
                {
                    batch.Add(playerEvent);
                    segmentStart = batch.Count;
                }

                if (++readCount == MaximumBatchSize)
                    await FlushAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            readCancellation.Cancel();
            // Flush 等待 UI 时可能已有一次异步 MoveNext 在进行。先结束读取，
            // 再释放枚举器，避免 async iterator 的并发 DisposeAsync 异常。
            if (pendingRead is not null)
            {
                try { await pendingRead.ConfigureAwait(false); }
                catch { /* 原始读取/派发异常由外层传播；清理不覆盖它。 */ }
            }
            await reader.DisposeAsync().ConfigureAwait(false);
        }

        async Task FlushAsync()
        {
            var ready = batch.ToArray();
            batch.Clear();
            segmentStart = 0;
            readCount = 0;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatch(() =>
            {
                try
                {
                    foreach (var playerEvent in ready)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        apply(playerEvent);
                    }
                    completion.TrySetResult();
                }
                catch (Exception exception) { completion.TrySetException(exception); }
            });
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
