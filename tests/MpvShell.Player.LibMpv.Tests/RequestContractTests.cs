using System.Collections.Concurrent;
using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class RequestContractTests
{
    [Fact]
    public async Task Cancellation_while_queued_should_skip_native_submission_and_allow_a_new_request()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var canceled = new CancellationTokenSource();
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await using var blocker = await SubmittedCommand.StartAsync(session, "19", timeout.Token);
        await blocker.CancelWaitAsync();

        var queued = session.SetPropertyAsync("volume", 99, canceled.Token);
        canceled.Cancel();
        await AssertCanceledAsync(queued);
        blocker.Release();

        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19);
        await session.SetPropertyAsync("mute", true, timeout.Token);
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
    }

    [Fact]
    public async Task Cancellation_at_the_queue_boundary_should_skip_the_canceled_native_request()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        for (var attempt = 0; attempt < 32; attempt++)
        {
            using var canceled = new CancellationTokenSource();
            await using var blocker = await SubmittedCommand.StartAsync(session, "19", timeout.Token, canceled.Cancel);
            await blocker.CancelWaitAsync();
            var queued = session.SetPropertyAsync("volume", 99, canceled.Token);

            // 在事件线程即将执行下一条排队请求时取消，验证这个紧邻提交的边界
            // 仍能阻止已取消请求改动原生音量。
            blocker.Release();
            await AssertCanceledAsync(queued);
            Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19,
                $"第 {attempt + 1} 次排队边界取消已发生在原生提交前");
        }
    }

    [Fact]
    public async Task Timeout_while_queued_should_skip_native_submission_and_allow_a_new_request()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = CreateSession(TimeSpan.FromMilliseconds(250));
        await session.InitializeAsync(timeout.Token);
        await using var blocker = await SubmittedCommand.StartAsync(session, "19", timeout.Token);
        await blocker.CancelWaitAsync();

        var queued = session.SetPropertyAsync("volume", 99, timeout.Token);
        await AssertTimedOutAsync(queued);
        blocker.Release();

        Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19);
        await session.SetPropertyAsync("mute", true, timeout.Token);
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_after_submission_should_preserve_native_outcome_and_ignore_its_late_reply(bool nativeError)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 19, timeout.Token);
        var events = new ConcurrentQueue<SessionEvent>();
        session.EventReceived += events.Enqueue;
        await using var submitted = await SubmittedCommand.StartAsync(session, nativeError ? "invalid-volume" : "41", timeout.Token);
        await submitted.CancelWaitAsync();

        var read = session.GetPropertyAsync("volume", timeout.Token);
        var mutation = session.SetPropertyAsync("mute", true, timeout.Token);
        read.IsCompleted.Should().BeFalse();
        mutation.IsCompleted.Should().BeFalse();
        submitted.Release();

        Convert.ToDouble(await read).Should().Be(nativeError ? 19 : 41,
            "迟到的命令回复不能把另一个属性查询完成为 null 或异常");
        await mutation;
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
        events.Should().NotContain(item => item.Error != null,
            "已取消请求的迟到错误不属于新的会话或媒体故障");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_after_submission_should_release_the_mutation_gate_and_ignore_its_late_reply(bool nativeError)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = CreateSession(TimeSpan.FromMilliseconds(250));
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 19, timeout.Token);
        var events = new ConcurrentQueue<SessionEvent>();
        session.EventReceived += events.Enqueue;
        await using var submitted = await SubmittedCommand.StartAsync(session, nativeError ? "invalid-volume" : "41", timeout.Token);
        await AssertTimedOutAsync(submitted.Request);

        var read = session.GetPropertyAsync("volume", timeout.Token);
        var mutation = session.SetPropertyAsync("mute", true, timeout.Token);
        read.IsCompleted.Should().BeFalse();
        mutation.IsCompleted.Should().BeFalse();
        submitted.Release();

        Convert.ToDouble(await read).Should().Be(nativeError ? 19 : 41);
        await mutation;
        (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
        events.Should().NotContain(item => item.Error != null);
    }

    private static MpvPlayerSession CreateSession(TimeSpan? requestTimeout = null) =>
        new(new Dictionary<string, string> { ["ao"] = "null" }, requestTimeout: requestTimeout);

    private static async Task AssertCanceledAsync(Task task)
    {
        Func<Task> wait = () => task;
        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task AssertTimedOutAsync(Task task)
    {
        Func<Task> wait = () => task;
        await wait.Should().ThrowAsync<TimeoutException>().WithMessage("播放器请求超时*");
    }

    private sealed class SubmittedCommand : IAsyncDisposable
    {
        private readonly CancellationTokenSource _waitCancellation;
        private readonly ManualResetEventSlim _release = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Request { get; private set; } = Task.CompletedTask;

        private SubmittedCommand(CancellationToken cancellationToken) =>
            _waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        internal static async Task<SubmittedCommand> StartAsync(MpvPlayerSession session, string value,
            CancellationToken cancellationToken, Action? beforeReturning = null)
        {
            var barrier = new SubmittedCommand(cancellationToken);
            barrier.Request = session.CommandAsync(["set", "volume", value], barrier._waitCancellation.Token, () =>
            {
                barrier._entered.TrySetResult();
                try
                {
                    if (!barrier._release.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("测试未及时释放事件线程。");
                    beforeReturning?.Invoke();
                }
                finally { barrier._exited.TrySetResult(); }
            });
            try
            {
                await barrier._entered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return barrier;
            }
            catch
            {
                barrier.Release();
                barrier._waitCancellation.Cancel();
                throw;
            }
        }

        internal async Task CancelWaitAsync()
        {
            _waitCancellation.Cancel();
            await AssertCanceledAsync(Request);
        }

        internal void Release() => _release.Set();

        public async ValueTask DisposeAsync()
        {
            Release();
            try { await _exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                _release.Dispose();
                _waitCancellation.Dispose();
            }
        }
    }
}
