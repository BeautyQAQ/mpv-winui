using System.Threading.Channels;
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelSeekTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Older_absolute_seek_reply_should_not_overwrite_the_newer_success(bool fails)
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.SeekToAsync(40);
        var second = vm.SeekToAsync(90);

        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Complete(requests[0], fails);
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(90);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Older_success_should_not_clear_an_error_from_the_newest_seek()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.SeekToAsync(40);
        var second = vm.SeekToAsync(90);
        requests[1].Completion.SetException(new InvalidOperationException("最新跳转失败"));
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        requests[0].Completion.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(20);
        vm.ErrorMessage.Should().Be("最新跳转失败");
    }

    [Fact]
    public async Task Older_success_should_not_apply_while_a_newer_seek_is_still_pending()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.SeekToAsync(40);
        var second = vm.SeekToAsync(90);
        try
        {
            requests[0].Completion.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));

            vm.State.PositionSeconds.Should().Be(20, "已有新请求时不显示旧请求的预计落点");
        }
        finally
        {
            requests[1].Completion.TrySetResult();
            await second.WaitAsync(TimeSpan.FromSeconds(5));
        }
        vm.State.PositionSeconds.Should().Be(90);
    }

    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 80)]
    public async Task Absolute_and_relative_seeks_should_share_the_same_reply_order(bool relativeFirst, double expected)
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = relativeFirst ? vm.SeekForwardCommand.ExecuteAsync(null) : vm.SeekToAsync(80);
        var second = relativeFirst ? vm.SeekToAsync(80) : vm.SeekBackwardCommand.ExecuteAsync(null);

        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        requests[0].Completion.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        requests.Select(request => request.IsRelative).Should().Equal(relativeFirst, !relativeFirst);
        vm.State.PositionSeconds.Should().Be(expected);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Consecutive_relative_seeks_without_position_events_should_accumulate_the_pending_target()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.HandleDragAsync(240, 0);
        var second = vm.HandleDragAsync(240, 0);

        requests[0].Completion.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        requests.Select(request => request.Value).Should().Equal(30, 30);
        requests.Should().OnlyContain(request => request.IsRelative);
        vm.State.PositionSeconds.Should().Be(80, "两个相对跳转不能都从尚未更新的20秒计算");
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 50)]
    public async Task Relative_seek_depending_on_an_earlier_failed_seek_should_wait_for_the_actual_position(
        bool firstRelative, double actualPosition)
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var first = firstRelative ? vm.HandleDragAsync(240, 0) : vm.SeekToAsync(80);
        var second = firstRelative ? vm.HandleDragAsync(240, 0) : vm.SeekBackwardCommand.ExecuteAsync(null);

        requests[0].Completion.SetException(new TimeoutException("前一次跳转等待超时"));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(20, "超时不保证撤销命令，依赖它的预计目标必须等待真实位置确认");
        vm.ErrorMessage.Should().BeNull("过时请求的错误不覆盖新请求的成功");

        await ApplyAsync(backend, callbacks, new PlaybackStateChanged(vm.State with { PositionSeconds = actualPosition }));
        vm.State.PositionSeconds.Should().Be(actualPosition);
    }

    [Fact]
    public async Task An_older_failed_seek_should_not_invalidate_an_independent_absolute_target()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.HandleDragAsync(240, 0);
        var second = vm.SeekToAsync(90);

        requests[0].Completion.SetException(new TimeoutException("前一次跳转等待超时"));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(90);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_successful_relative_reply_should_wait_for_its_pending_predecessor_before_estimating_a_position()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.HandleDragAsync(240, 0);
        var second = vm.HandleDragAsync(240, 0);
        requests[1].Completion.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            vm.State.PositionSeconds.Should().Be(20, "前置请求尚未确认时不能回填依赖它的预计80秒");
        }
        finally
        {
            requests[0].Completion.TrySetException(new TimeoutException("前一次跳转等待超时"));
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }

        vm.State.PositionSeconds.Should().Be(20);
        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_position_event_before_the_reply_should_win_over_the_estimated_seek_target(bool relative)
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var seek = relative ? vm.SeekForwardCommand.ExecuteAsync(null) : vm.SeekToAsync(50);

        await ApplyAsync(backend, callbacks, new PlaybackStateChanged(vm.State with { PositionSeconds = 51.25 }));
        requests[0].Completion.SetResult();
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(51.25, "命令确认不能把已经观察到的实际播放位置改回预计值");
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task A_non_position_event_should_not_discard_the_pending_seek_target()
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var seek = vm.SeekToAsync(50);

        await ApplyAsync(backend, callbacks, new PlaybackStateChanged(vm.State with { IsPlaying = false, Volume = 42 }));
        requests[0].Completion.SetResult();
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(50);
        vm.State.Volume.Should().Be(42);
        vm.State.IsPlaying.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Starting_a_new_load_should_invalidate_old_seek_replies_even_for_the_same_url(bool sameUrl, bool fails)
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var loadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.LoadHandler = (_, _) => loadCompletion.Task;
        await using var vm = CreateViewModel(backend);
        var seek = vm.SeekToAsync(80);
        vm.UrlText = sameUrl ? "current.mp4" : "next.mp4";
        var load = vm.OpenUrlCommand.ExecuteAsync(null);
        try
        {
            Complete(requests[0], fails);
            await seek.WaitAsync(TimeSpan.FromSeconds(5));

            vm.State.PositionSeconds.Should().Be(20, "换媒体从发起加载时隔离旧跳转，不能等待新URL出现在状态中");
            vm.ErrorMessage.Should().BeNull();
        }
        finally
        {
            loadCompletion.TrySetResult();
            await load.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task A_backend_media_change_should_invalidate_old_seek_replies()
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var seek = vm.SeekToAsync(80);

        await ApplyAsync(backend, callbacks, new PlaybackStateChanged(vm.State with
        {
            CurrentUrl = "redirected.mp4", PositionSeconds = 3,
        }));
        requests[0].Completion.SetException(new InvalidOperationException("旧媒体跳转失败"));
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.CurrentUrl.Should().Be("redirected.mp4");
        vm.State.PositionSeconds.Should().Be(3);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task End_of_media_should_invalidate_a_seek_reply_that_was_already_pending()
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var seek = vm.SeekToAsync(80);

        await ApplyAsync(backend, callbacks, new EndReached());
        var endedState = vm.State;
        requests[0].Completion.SetResult();
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.Should().Be(endedState);
    }

    [Fact]
    public async Task A_playback_fault_should_not_be_cleared_by_an_older_successful_seek_reply()
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var seek = vm.SeekToAsync(80);

        await ApplyAsync(backend, callbacks, new BackendFaulted("媒体读取失败", PlayerErrorKind.Playback));
        var failedState = vm.State;
        requests[0].Completion.SetResult();
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.ErrorMessage.Should().Be("媒体读取失败");
        vm.State.Should().Be(failedState);
    }

    [Fact]
    public async Task Seek_failure_after_disposal_should_not_update_bound_state_or_error()
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var seek = vm.SeekToAsync(80);
        await vm.DisposeAsync();
        var disposedState = vm.State;

        requests[0].Completion.SetException(new InvalidOperationException("关闭后到达的跳转失败"));
        await seek.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.Should().Be(disposedState);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_latest_seek_should_leave_the_next_relative_seek_waiting_for_actual_position()
    {
        var requests = new List<PendingSeek>();
        var backend = CreateBackend(requests);
        var callbacks = Channel.CreateUnbounded<Action>();
        await using var vm = CreateViewModel(backend, callbacks);
        await vm.InitializeAsync();
        var failed = vm.SeekToAsync(80);
        requests[0].Completion.SetException(new TimeoutException("跳转等待超时"));
        await failed.WaitAsync(TimeSpan.FromSeconds(5));

        var next = vm.SeekForwardCommand.ExecuteAsync(null);
        requests[1].Completion.SetResult();
        await next.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(20, "超时后不能假定内核仍停在界面显示的旧位置");
        vm.ErrorMessage.Should().BeNull();

        await ApplyAsync(backend, callbacks, new PlaybackStateChanged(vm.State with { PositionSeconds = 50 }));
        vm.State.PositionSeconds.Should().Be(50);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_latest_failure_should_keep_following_relative_estimates_invalid_regardless_of_earlier_success_order(
        bool earlierCompletedBeforeFailure)
    {
        var requests = new List<PendingSeek>();
        await using var vm = CreateViewModel(CreateBackend(requests));
        var first = vm.SeekToAsync(80);
        var second = vm.SeekBackwardCommand.ExecuteAsync(null);
        if (earlierCompletedBeforeFailure)
        {
            requests[0].Completion.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }

        requests[1].Completion.SetException(new TimeoutException("第二次跳转等待超时"));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        var third = vm.SeekForwardCommand.ExecuteAsync(null);
        if (!earlierCompletedBeforeFailure)
        {
            requests[0].Completion.SetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }
        requests[2].Completion.SetResult();
        await third.WaitAsync(TimeSpan.FromSeconds(5));

        vm.State.PositionSeconds.Should().Be(20, "前置成功和最新失败都不能让后续相对请求从陈旧界面位置重新猜测");
        vm.ErrorMessage.Should().BeNull();
    }

    private static EventPublishingBackend CreateBackend(List<PendingSeek> requests)
    {
        Task Add(bool relative, double value)
        {
            var request = new PendingSeek(relative, value);
            requests.Add(request);
            return request.Completion.Task;
        }
        return new EventPublishingBackend
        {
            SeekHandler = (value, _) => Add(true, value),
            SetPositionHandler = (value, _) => Add(false, value),
        };
    }

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, Channel<Action>? callbacks = null)
    {
        var vm = new PlayerViewModel(backend, new PlaybackInteractionCoordinator())
        {
            State = PlaybackState.Initial with
            {
                CurrentUrl = "current.mp4", PositionSeconds = 20, DurationSeconds = 120, IsPlaying = true,
            },
        };
        if (callbacks is not null) vm.SetUiDispatcher(callback => callbacks.Writer.TryWrite(callback));
        return vm;
    }

    private static async Task ApplyAsync(EventPublishingBackend backend, Channel<Action> callbacks, PlayerEvent playerEvent)
    {
        backend.Publish(playerEvent);
        (await callbacks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))();
    }

    private static void Complete(PendingSeek request, bool fails)
    {
        if (fails) request.Completion.SetException(new InvalidOperationException("旧跳转失败"));
        else request.Completion.SetResult();
    }

    private sealed record PendingSeek(bool IsRelative, double Value)
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
