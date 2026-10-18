using FluentAssertions;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelRecentHistoryTests
{
    [Fact]
    public async Task Initialize_should_restore_order_without_loading_media_or_waiting_for_the_disk()
    {
        var read = Completion<IReadOnlyList<string>>();
        var loaded = false;
        var storage = new TestStorage { ReadHandler = _ => read.Task.WaitAsync(TimeSpan.FromSeconds(10)) };
        var backend = new EventPublishingBackend { LoadHandler = (_, _) => { loaded = true; return Task.CompletedTask; } };
        await using var vm = CreateViewModel(backend, storage);

        await vm.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));

        vm.IsInitialized.Should().BeTrue("历史读取不应阻止后端初始化");
        vm.RecentUrls.Should().BeEmpty();
        read.SetResult(["https://media.example/new.mp4", "https://media.example/old.mp4"]);
        await vm.WaitForRecentHistoryAsync();

        vm.RecentUrls.Should().Equal("https://media.example/new.mp4", "https://media.example/old.mp4");
        vm.RecentEmptyVisibility.Should().Be(Visibility.Collapsed);
        vm.State.CurrentUrl.Should().BeNull();
        loaded.Should().BeFalse("恢复历史不应自动打开媒体");
        storage.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_read_failure_should_not_prevent_startup_or_become_a_playback_error()
    {
        var storage = new TestStorage { ReadHandler = _ => Task.FromException<IReadOnlyList<string>>(new IOException("无法读取")) };
        await using var vm = CreateViewModel(new EventPublishingBackend(), storage);

        await vm.InitializeAsync();
        await vm.WaitForRecentHistoryAsync();

        vm.IsInitialized.Should().BeTrue();
        vm.ErrorMessage.Should().BeNull();
        vm.RecentHistoryMessage.Should().NotBeNullOrWhiteSpace();
        vm.RecentHistoryMessageVisibility.Should().Be(Visibility.Visible);
        vm.RecentEmptyVisibility.Should().Be(Visibility.Visible);

        await OpenAsync(vm, "https://media.example/new.mp4");
        await vm.WaitForRecentHistoryAsync();
        vm.State.CurrentUrl.Should().Be("https://media.example/new.mp4");
        vm.RecentUrls.Should().Equal("https://media.example/new.mp4");
        vm.RecentHistoryMessage.Should().BeNull("后续成功保存可恢复持久化");
    }

    [Fact]
    public async Task Successful_load_should_save_the_trimmed_source_and_preserve_existing_order()
    {
        var storage = new TestStorage { InitialItems = ["https://media.example/old.mp4", "https://media.example/first.mp4"] };
        await using var vm = CreateViewModel(new EventPublishingBackend(), storage);

        await vm.InitializeAsync();
        await OpenAsync(vm, "  https://media.example/first.mp4  ");
        await vm.WaitForRecentHistoryAsync();

        vm.RecentUrls.Should().Equal("https://media.example/first.mp4", "https://media.example/old.mp4");
        storage.Writes.Should().ContainSingle().Which.Should().Equal("https://media.example/first.mp4", "https://media.example/old.mp4");
        storage.WriteTokens.Should().OnlyContain(token => !token.CanBeCanceled);
        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("info")]
    [InlineData("tracks")]
    public async Task A_failed_open_should_not_record_history(string stage)
    {
        var failure = new InvalidOperationException("媒体加载失败");
        var storage = new TestStorage();
        var backend = new EventPublishingBackend
        {
            LoadHandler = (_, _) => stage == "load" ? Task.FromException(failure) : Task.CompletedTask,
            InfoHandler = _ => stage == "info" ? Task.FromException<InfoPanelSnapshot>(failure) : Task.FromResult(EmptyInfo),
            TracksHandler = _ => stage == "tracks" ? Task.FromException<IReadOnlyList<TrackInfo>>(failure) : Task.FromResult<IReadOnlyList<TrackInfo>>([]),
        };
        await using var vm = CreateViewModel(backend, storage);

        await OpenAsync(vm, "https://media.example/broken.mp4");
        await vm.WaitForRecentHistoryAsync();

        vm.RecentUrls.Should().BeEmpty();
        storage.Writes.Should().BeEmpty();
        vm.ErrorMessage.Should().Be("媒体加载失败");
        vm.RecentHistoryMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("info")]
    [InlineData("tracks")]
    public async Task An_obsolete_open_should_not_record_history(string stage)
    {
        var blocked = new BlockedMediaOperation(stage);
        var storage = new TestStorage();
        await using var vm = CreateViewModel(blocked.Backend, storage);
        var first = OpenAsync(vm, "https://media.example/first.mp4");
        blocked.UseImmediateResults = true;

        await OpenAsync(vm, "https://media.example/new.mp4");
        blocked.Complete();
        await first;
        await vm.WaitForRecentHistoryAsync();

        vm.RecentUrls.Should().Equal("https://media.example/new.mp4");
        storage.Writes.Should().ContainSingle().Which.Should().Equal("https://media.example/new.mp4");
        vm.State.CurrentUrl.Should().Be("https://media.example/new.mp4");
        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("load")]
    [InlineData("info")]
    [InlineData("tracks")]
    public async Task Clearing_during_an_open_should_preserve_playback_but_not_restore_pre_clear_history(string stage)
    {
        var blocked = new BlockedMediaOperation(stage);
        var storage = new TestStorage { InitialItems = ["https://media.example/old.mp4"] };
        await using var vm = CreateViewModel(blocked.Backend, storage);
        await vm.InitializeAsync();
        var first = OpenAsync(vm, "https://media.example/first.mp4");

        await vm.ClearRecentHistoryCommand.ExecuteAsync(null);
        blocked.LoadToken.IsCancellationRequested.Should().BeFalse("清空历史不应取消媒体播放");
        blocked.Complete();
        await first;
        await vm.WaitForRecentHistoryAsync();

        vm.State.CurrentUrl.Should().Be("https://media.example/first.mp4");
        vm.RecentUrls.Should().BeEmpty();
        storage.Writes.Should().ContainSingle().Which.Should().BeEmpty();
        vm.RecentEmptyVisibility.Should().Be(Visibility.Visible);

        blocked.UseImmediateResults = true;
        await OpenAsync(vm, "https://media.example/new.mp4");
        await vm.WaitForRecentHistoryAsync();
        vm.RecentUrls.Should().Equal("https://media.example/new.mp4");
    }

    [Fact]
    public async Task A_slow_save_should_not_block_stop_or_a_new_open_or_replace_its_error()
    {
        var write = Completion();
        var stopCalls = 0;
        var storage = new TestStorage { WriteHandler = (_, _) => write.Task.WaitAsync(TimeSpan.FromSeconds(10)) };
        var backend = new EventPublishingBackend
        {
            StopHandler = _ => { stopCalls++; return Task.CompletedTask; },
            LoadHandler = (url, _) => url == "https://media.example/broken.mp4" ? Task.FromException(new IOException("新媒体失败")) : Task.CompletedTask,
        };
        await using var vm = CreateViewModel(backend, storage);

        await OpenAsync(vm, "https://media.example/first.mp4").WaitAsync(TimeSpan.FromSeconds(5));
        vm.OpenUrlCommand.IsRunning.Should().BeFalse();
        await vm.StopCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        stopCalls.Should().Be(1);
        await OpenAsync(vm, "https://media.example/new.mp4").WaitAsync(TimeSpan.FromSeconds(5));
        await OpenAsync(vm, "https://media.example/broken.mp4");
        var currentState = vm.State;
        var currentInput = vm.UrlText;
        write.SetResult();
        await vm.WaitForRecentHistoryAsync();

        vm.State.Should().Be(currentState);
        vm.UrlText.Should().Be(currentInput);
        vm.ErrorMessage.Should().Be("新媒体失败");
        vm.RecentUrls.Should().Equal("https://media.example/new.mp4", "https://media.example/first.mp4");
    }

    [Fact]
    public async Task Clear_should_follow_an_accepted_save_and_precede_subsequent_open_history()
    {
        var firstWrite = Completion();
        var clearWrite = Completion();
        var clearStarted = Completion();
        var storage = new TestStorage
        {
            WriteHandler = (items, _) =>
            {
                if (items.Count == 0) { clearStarted.TrySetResult(); return clearWrite.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                return items[0] == "https://media.example/first.mp4" ? firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(10)) : Task.CompletedTask;
            },
        };
        await using var vm = CreateViewModel(new EventPublishingBackend(), storage);
        await OpenAsync(vm, "https://media.example/first.mp4");

        var clear = vm.ClearRecentHistoryCommand.ExecuteAsync(null);
        vm.RecentUrls.Should().BeEmpty();
        await OpenAsync(vm, "https://media.example/new.mp4");
        firstWrite.SetResult();
        await clearStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.RecentUrls.Should().BeEmpty("清空前的迟到保存不能重新填回旧记录");
        clearWrite.SetResult();
        await clear;
        await vm.WaitForRecentHistoryAsync();

        storage.Writes.Should().HaveCount(3);
        storage.Writes[0].Should().Equal("https://media.example/first.mp4");
        storage.Writes[1].Should().BeEmpty();
        storage.Writes[2].Should().Equal("https://media.example/new.mp4");
        vm.RecentUrls.Should().Equal("https://media.example/new.mp4");
    }

    [Fact]
    public async Task Clearing_during_startup_read_should_not_restore_the_loaded_history()
    {
        var read = Completion<IReadOnlyList<string>>();
        var write = Completion();
        var writeStarted = Completion();
        var storage = new TestStorage
        {
            ReadHandler = _ => read.Task.WaitAsync(TimeSpan.FromSeconds(10)),
            WriteHandler = (_, _) => { writeStarted.TrySetResult(); return write.Task.WaitAsync(TimeSpan.FromSeconds(10)); },
        };
        await using var vm = CreateViewModel(new EventPublishingBackend(), storage);
        await vm.InitializeAsync();

        var clear = vm.ClearRecentHistoryCommand.ExecuteAsync(null);
        read.SetResult(["https://media.example/old.mp4"]);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.RecentUrls.Should().BeEmpty();
        write.SetResult();
        await clear;
        await vm.WaitForRecentHistoryAsync();
        storage.Writes.Should().ContainSingle().Which.Should().BeEmpty();
        vm.RecentUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_failure_should_remain_separate_from_playback_and_allow_a_later_retry()
    {
        var storage = new TestStorage { WriteHandler = (_, _) => Task.FromException(new IOException("无法写入")) };
        await using var vm = CreateViewModel(new EventPublishingBackend(), storage);

        await OpenAsync(vm, "https://media.example/first.mp4");
        await vm.WaitForRecentHistoryAsync();

        vm.State.CurrentUrl.Should().Be("https://media.example/first.mp4");
        vm.RecentUrls.Should().Equal("https://media.example/first.mp4");
        vm.ErrorMessage.Should().BeNull();
        vm.RecentHistoryMessage.Should().NotBeNullOrWhiteSpace();

        storage.WriteHandler = null;
        await vm.ClearRecentHistoryCommand.ExecuteAsync(null);
        vm.RecentUrls.Should().BeEmpty();
        vm.RecentHistoryMessage.Should().BeNull();
        vm.RecentHistoryMessageVisibility.Should().Be(Visibility.Collapsed);
        vm.State.CurrentUrl.Should().Be("https://media.example/first.mp4");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_should_release_backend_then_wait_for_history_without_updating_closed_ui(bool initializing)
    {
        var write = Completion();
        var read = Completion<IReadOnlyList<string>>();
        var storage = new TestStorage
        {
            ReadHandler = initializing ? _ => read.Task.WaitAsync(TimeSpan.FromSeconds(10)) : null,
            WriteHandler = (_, _) => write.Task.WaitAsync(TimeSpan.FromSeconds(10)),
        };
        var backend = new EventPublishingBackend();
        var vm = CreateViewModel(backend, storage);
        await vm.InitializeAsync();
        if (!initializing) await OpenAsync(vm, "https://media.example/first.mp4");
        var changed = false;
        vm.PropertyChanged += (_, _) => changed = true;
        vm.RecentUrls.CollectionChanged += (_, _) => changed = true;

        var disposal = vm.DisposeAsync().AsTask();
        await WaitUntilAsync(() => backend.DisposeCalls == 1);
        disposal.IsCompleted.Should().BeFalse("关闭应等待已接受的历史读取或保存完成");
        read.TrySetResult(["https://media.example/old.mp4"]);
        write.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        changed.Should().BeFalse("迟到的历史任务不得更改已关闭界面");
        backend.DisposeCalls.Should().Be(1);
        if (!initializing) storage.Writes.Should().ContainSingle().Which.Should().Equal("https://media.example/first.mp4");
        await vm.DisposeAsync();
    }

    private static PlayerViewModel CreateViewModel(EventPublishingBackend backend, IRecentUrlStorage storage) =>
        new(backend, new PlaybackInteractionCoordinator(), new GestureDecisionEngine(), new RecentUrlStore(storage), new InfoPanelViewModel());

    private static Task OpenAsync(PlayerViewModel vm, string url)
    {
        vm.UrlText = url;
        return vm.OpenUrlCommand.ExecuteAsync(null);
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static InfoPanelSnapshot EmptyInfo => new(null, null, null, null, null, null, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class TestStorage : IRecentUrlStorage
    {
        internal IReadOnlyList<string> InitialItems { get; init; } = [];
        internal Func<CancellationToken, Task<IReadOnlyList<string>>>? ReadHandler { get; init; }
        internal Func<IReadOnlyList<string>, CancellationToken, Task>? WriteHandler { get; set; }
        internal List<IReadOnlyList<string>> Writes { get; } = [];
        internal List<CancellationToken> WriteTokens { get; } = [];

        public Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken) =>
            ReadHandler?.Invoke(cancellationToken) ?? Task.FromResult(InitialItems);

        public Task WriteAsync(IReadOnlyList<string> items, CancellationToken cancellationToken)
        {
            Writes.Add(items.ToArray());
            WriteTokens.Add(cancellationToken);
            return WriteHandler?.Invoke(items, cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class BlockedMediaOperation
    {
        private readonly TaskCompletionSource _load = Completion();
        private readonly TaskCompletionSource<InfoPanelSnapshot> _info = Completion<InfoPanelSnapshot>();
        private readonly TaskCompletionSource<IReadOnlyList<TrackInfo>> _tracks = Completion<IReadOnlyList<TrackInfo>>();
        internal bool UseImmediateResults { get; set; }
        internal CancellationToken LoadToken { get; private set; }
        internal EventPublishingBackend Backend { get; }

        internal BlockedMediaOperation(string stage)
        {
            Backend = new()
            {
                LoadHandler = (_, token) =>
                {
                    LoadToken = token;
                    return !UseImmediateResults && stage == "load" ? _load.Task.WaitAsync(TimeSpan.FromSeconds(10)) : Task.CompletedTask;
                },
                InfoHandler = _ => !UseImmediateResults && stage == "info" ? _info.Task.WaitAsync(TimeSpan.FromSeconds(10)) : Task.FromResult(EmptyInfo),
                TracksHandler = _ => !UseImmediateResults && stage == "tracks" ? _tracks.Task.WaitAsync(TimeSpan.FromSeconds(10)) : Task.FromResult<IReadOnlyList<TrackInfo>>([]),
            };
        }

        internal void Complete()
        {
            _load.TrySetResult();
            _info.TrySetResult(EmptyInfo);
            _tracks.TrySetResult([]);
        }
    }
}
