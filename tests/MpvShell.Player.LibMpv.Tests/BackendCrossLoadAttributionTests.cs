using System.Collections.Concurrent;
using FluentAssertions;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

/// <summary>
/// 跨加载归属审计：旧媒体被替换时必须先发 EndFile 再发新媒体的 StartFile，
/// 新加载不能因旧媒体的起止尾部事件被错误终止。真实会话加显式屏障，
/// 不依赖任意休眠制造竞态；fillers 用于拉长核心处理同一批命令的窗口。
/// </summary>
public sealed class BackendCrossLoadAttributionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    public async Task Replaced_loads_should_keep_ordered_start_end_pairs_and_the_next_load_must_survive(int fillers)
    {
        const int iterations = 60;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        var order = new ConcurrentQueue<string>();
        session.EventReceived += item => order.Enqueue(item.Id.ToString());
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var local = Path.Combine(Path.GetTempPath(), "mpvshell-attribution-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteWave(local, 3);
        try
        {
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                await using var held = new HttpMediaServer(holdMedia: true, durationSeconds: 7);
                using var cancel = new CancellationTokenSource();
                var heldLoad = backend.LoadUrlAsync(held.MediaUrl, cancel.Token);
                await held.MediaRequested.WaitAsync(timeout.Token);
                var fillerTasks = new List<Task<object?>>();
                for (var filler = 0; filler < fillers; filler++)
                    fillerTasks.Add(session.CommandAsync(["set", "volume", "50"], CancellationToken.None));
                cancel.Cancel();
                var waitHeld = () => heldLoad;
                await waitHeld.Should().ThrowAsync<OperationCanceledException>();

                var next = backend.LoadUrlAsync(local, timeout.Token);
                var failure = await Record.ExceptionAsync(() => next);
                await Task.WhenAll(fillerTasks);
                failure.Should().BeNull(
                    $"iteration={iteration} fillers={fillers} 尾迹：{string.Join(", ", order.TakeLast(30))}");
                held.ReleaseMedia();
            }

            (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(local);

            var ids = order.Where(id => id is "StartFile" or "EndFile").ToList();
            var starts = ids.Count(id => id == "StartFile");
            var ends = ids.Count(id => id == "EndFile");
            starts.Should().BeGreaterThanOrEqualTo(iterations);
            ends.Should().BeGreaterThanOrEqualTo(iterations - 1);
            (starts - ends).Should().BeInRange(0, 1);
            var mismatch = Enumerable.Range(0, ids.Count)
                .FirstOrDefault(index => ids[index] != (index % 2 == 0 ? "StartFile" : "EndFile"), -1);
            mismatch.Should().Be(-1,
                $"StartFile/EndFile 必须严格交替；前 60 项：{string.Join(", ", ids.Take(60))}");
        }
        finally
        {
            File.Delete(local);
        }
    }

    [Fact]
    public async Task A_stale_start_and_end_delivered_after_submission_must_not_terminate_the_pending_load()
    {
        await using var held = new HttpMediaServer(holdMedia: true, durationSeconds: 7);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        var startBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseStart = new ManualResetEventSlim();
        var startCount = 0;
        session.EventReceived += BlockFirstStart;
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(timeout.Token);
            var load = backend.LoadUrlAsync(held.MediaUrl, timeout.Token);
            await startBlocked.Task.WaitAsync(timeout.Token);

            // 托管事件注入：在新加载已受理、其 StartFile 尚未送达时，
            // 模拟旧媒体的迟到 Start/End 对（源码顺序分析中的可达窗口）。
            backend.OnSessionEvent(new(MpvEventId.StartFile));
            backend.OnSessionEvent(new(MpvEventId.EndFile, Value: 2));

            releaseStart.Set();
            held.ReleaseMedia();
            await load.WaitAsync(timeout.Token);
            (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(held.MediaUrl);
        }
        finally
        {
            releaseStart.Set();
            held.ReleaseMedia();
            session.EventReceived -= BlockFirstStart;
        }

        void BlockFirstStart(SessionEvent item)
        {
            if (item.Id != MpvEventId.StartFile || Interlocked.Increment(ref startCount) != 1) return;
            startBlocked.TrySetResult();
            releaseStart.Wait(timeout.Token);
        }
    }

    [Fact]
    public async Task Rapid_cancel_and_replace_should_not_fault_the_next_load()
    {
        const int iterations = 40;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        await backend.InitializeAsync(timeout.Token);
        var local = Path.Combine(Path.GetTempPath(), "mpvshell-attribution-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteWave(local, 3);
        try
        {
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                await using var held = new HttpMediaServer(holdMedia: true, durationSeconds: 7);
                using var cancel = new CancellationTokenSource();
                var fillerTasks = new List<Task<object?>>();
                for (var filler = 0; filler < 192; filler++)
                    fillerTasks.Add(session.CommandAsync(["set", "volume", "50"], CancellationToken.None));
                var heldLoad = backend.LoadUrlAsync(held.MediaUrl, cancel.Token);
                cancel.Cancel();
                var waitHeld = () => heldLoad;
                await waitHeld.Should().ThrowAsync<OperationCanceledException>();

                var next = backend.LoadUrlAsync(local, timeout.Token);
                var failure = await Record.ExceptionAsync(() => next);
                await Task.WhenAll(fillerTasks);
                failure.Should().BeNull($"iteration={iteration}");
                (await session.GetPropertyAsync("path", timeout.Token)).Should().Be(local, $"iteration={iteration}");
                held.ReleaseMedia();
            }
        }
        finally
        {
            File.Delete(local);
        }
    }

    private static void WriteWave(string path, int durationSeconds)
    {
        const int sampleRate = 44100;
        var dataLength = sampleRate * durationSeconds * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(dataLength + 36);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }
}
