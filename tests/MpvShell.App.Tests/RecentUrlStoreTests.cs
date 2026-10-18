using FluentAssertions;
using MpvShell.App.Services;
using System.Text.Json;

namespace MpvShell.App.Tests;

public sealed class RecentUrlStoreTests
{
    [Fact]
    public async Task Add_should_move_duplicate_url_to_front()
    {
        var store = new RecentUrlStore();

        await store.AddAsync("https://a.example/1.m3u8");
        await store.AddAsync("https://b.example/2.m3u8");
        await store.AddAsync("https://a.example/1.m3u8");

        store.Items.Should().Equal("https://a.example/1.m3u8", "https://b.example/2.m3u8");
    }

    [Fact]
    public async Task Add_should_keep_only_ten_most_recent_items_and_preserve_ordinal_identity()
    {
        var store = new RecentUrlStore();
        for (var index = 0; index < 12; index++)
            await store.AddAsync($"https://example.com/{index}.mp4");

        store.Items.Should().Equal(Enumerable.Range(2, 10).Reverse().Select(index => $"https://example.com/{index}.mp4"));
        await store.AddAsync("https://EXAMPLE.com/11.mp4");
        store.Items.Take(2).Should().Equal("https://EXAMPLE.com/11.mp4", "https://example.com/11.mp4");
        store.Items.Should().HaveCount(10);
    }

    [Fact]
    public async Task Initialize_should_filter_invalid_entries_preserve_missing_local_paths_and_read_once()
    {
        var localPath = Path.Combine(Path.GetTempPath(), $"不存在-{Guid.NewGuid():N}.mp4");
        var storage = new FakeStorage
        {
            StoredItems = ["", " ", null!, "ftp://example.com/a.mp4", "file:///C:/a.mp4", "relative.mp4", "ytdl://anything",
                "https://example.com/a.mp4", "https://example.com/a.mp4", localPath,
                "https://example.com/" + new string('a', 32768), "https://example.com/a\0.mp4", "http://example.com/b.mp4"]
        };
        var store = new RecentUrlStore(storage);

        await store.InitializeAsync();
        await store.InitializeAsync();

        store.Items.Should().Equal("https://example.com/a.mp4", localPath, "http://example.com/b.mp4");
        storage.ReadCount.Should().Be(1);
        storage.WrittenSnapshots.Should().BeEmpty();
        store.PersistenceError.Should().BeNull();
    }

    [Fact]
    public async Task Initialize_should_limit_recovered_history_to_ten_distinct_items()
    {
        var expected = Enumerable.Range(0, 10).Select(index => $"https://example.com/{index}.mp4").ToArray();
        var storage = new FakeStorage { StoredItems = expected.Concat(expected).Append("https://example.com/extra.mp4").ToArray() };
        var store = new RecentUrlStore(storage);

        await store.InitializeAsync();

        store.Items.Should().Equal(expected);
    }

    [Fact]
    public async Task Add_should_share_backend_length_boundary_and_reject_embedded_nul()
    {
        const string prefix = "https://example.com/";
        var maximumLengthUrl = prefix + new string('a', 32768 - prefix.Length);
        var store = new RecentUrlStore();

        await store.AddAsync(maximumLengthUrl);
        await store.AddAsync(maximumLengthUrl + "b");
        await store.AddAsync("https://example.com/invalid\0.mp4");

        store.Items.Should().Equal(maximumLengthUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.mp4")]
    [InlineData("ftp://example.com/a.mp4")]
    [InlineData("file:///C:/a.mp4")]
    [InlineData("javascript:alert(1)")]
    public async Task Add_should_ignore_invalid_source_without_writing(string source)
    {
        var storage = new FakeStorage();
        var store = new RecentUrlStore(storage);

        await store.AddAsync(source);

        store.Items.Should().BeEmpty();
        storage.WrittenSnapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_should_initialize_then_save_and_expose_detached_readonly_snapshots()
    {
        var storage = new FakeStorage { StoredItems = ["https://example.com/old.mp4"] };
        var store = new RecentUrlStore(storage);
        await store.InitializeAsync();
        var earlierItems = store.Items;

        await store.AddAsync("https://example.com/new.mp4");

        store.Items.Should().Equal("https://example.com/new.mp4", "https://example.com/old.mp4");
        storage.StoredItems.Should().Equal(store.Items);
        earlierItems.Should().Equal("https://example.com/old.mp4");
        Action mutation = () => ((IList<string>)store.Items)[0] = "https://example.com/changed.mp4";
        mutation.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Clear_before_initialization_should_persist_empty_history()
    {
        var storage = new FakeStorage { StoredItems = ["https://example.com/old.mp4"] };
        var store = new RecentUrlStore(storage);

        await store.ClearAsync();
        var restarted = new RecentUrlStore(storage);
        await restarted.InitializeAsync();

        store.Items.Should().BeEmpty();
        restarted.Items.Should().BeEmpty();
        storage.WrittenSnapshots.Should().ContainSingle().Which.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_failure_should_allow_later_write_and_not_expose_sensitive_exception_message(bool malformedJson)
    {
        var storage = new FakeStorage
        {
            Read = _ => Task.FromException<IReadOnlyList<string>>(malformedJson
                ? new JsonException("https://example.com/private?token=secret")
                : new UnauthorizedAccessException("https://example.com/private?token=secret"))
        };
        var store = new RecentUrlStore(storage);

        await store.InitializeAsync();

        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().NotBeNull().And.NotContain("secret").And.NotContain("example.com");
        await store.AddAsync("https://example.com/new.mp4");
        store.Items.Should().Equal("https://example.com/new.mp4");
        storage.StoredItems.Should().Equal(store.Items);
        store.PersistenceError.Should().BeNull();
    }

    [Fact]
    public async Task Write_failure_should_keep_current_memory_and_report_that_old_history_may_return()
    {
        var storage = new FakeStorage
        {
            StoredItems = ["https://example.com/old.mp4"],
            Write = (_, _) => Task.FromException(new IOException("https://example.com/private?token=secret"))
        };
        var store = new RecentUrlStore(storage);

        await store.AddAsync("https://example.com/new.mp4");

        store.Items.Should().Equal("https://example.com/new.mp4", "https://example.com/old.mp4");
        storage.StoredItems.Should().Equal("https://example.com/old.mp4");
        store.PersistenceError.Should().Contain("未能保存").And.NotContain("secret");
        await store.ClearAsync();
        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().Contain("此前记录");

        storage.Write = null;
        await store.ClearAsync();
        storage.StoredItems.Should().BeEmpty();
        store.PersistenceError.Should().BeNull();
    }

    [Fact]
    public async Task Pending_initialization_should_not_overwrite_queued_add_or_clear()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage { Read = token => { started.SetResult(); return read.Task.WaitAsync(token); } };
        var store = new RecentUrlStore(storage);

        var initialize = store.InitializeAsync();
        await started.Task;
        var add = store.AddAsync("https://example.com/new.mp4");
        var clear = store.ClearAsync();
        add.IsCompleted.Should().BeFalse();
        clear.IsCompleted.Should().BeFalse();
        read.SetResult(["https://example.com/old.mp4"]);
        await Task.WhenAll(initialize, add, clear);

        storage.ReadCount.Should().Be(1);
        storage.WrittenSnapshots.Should().HaveCount(2);
        storage.WrittenSnapshots[0].Should().Equal("https://example.com/new.mp4", "https://example.com/old.mp4");
        storage.WrittenSnapshots[1].Should().BeEmpty();
        store.Items.Should().BeEmpty();
        storage.StoredItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_should_wait_for_older_write_and_be_the_final_persisted_snapshot()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage
        {
            Write = async (items, token) =>
            {
                if (items.Count == 0) return;
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
        };
        var store = new RecentUrlStore(storage);

        var add = store.AddAsync("https://example.com/new.mp4");
        await started.Task;
        var clear = store.ClearAsync();
        clear.IsCompleted.Should().BeFalse();
        storage.WrittenSnapshots.Should().ContainSingle();
        release.SetResult();
        await Task.WhenAll(add, clear);

        store.Items.Should().BeEmpty();
        storage.StoredItems.Should().BeEmpty();
        storage.WrittenSnapshots.Last().Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_waiter_should_not_modify_history_or_block_following_operations()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage { Read = async token => { started.SetResult(); await release.Task.WaitAsync(token); return []; } };
        var store = new RecentUrlStore(storage);
        var initialize = store.InitializeAsync();
        await started.Task;
        using var cancellation = new CancellationTokenSource();
        var add = store.AddAsync("https://example.com/cancelled.mp4", cancellation.Token);
        cancellation.Cancel();

        await ((Func<Task>)(() => add)).Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();
        await initialize;
        await store.AddAsync("https://example.com/accepted.mp4");

        store.Items.Should().Equal("https://example.com/accepted.mp4");
        storage.WrittenSnapshots.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelled_read_should_propagate_and_allow_initialization_retry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage
        {
            Read = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return []; }
        };
        var store = new RecentUrlStore(storage);
        using var cancellation = new CancellationTokenSource();
        var initialize = store.InitializeAsync(cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await ((Func<Task>)(() => initialize)).Should().ThrowAsync<OperationCanceledException>();
        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().BeNull();
        storage.Read = null;
        storage.StoredItems = ["https://example.com/recovered.mp4"];
        await store.InitializeAsync();

        storage.ReadCount.Should().Be(2);
        store.Items.Should().Equal("https://example.com/recovered.mp4");
    }

    [Fact]
    public async Task Cancelled_write_should_propagate_keep_accepted_memory_and_release_serialization_gate()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage
        {
            StoredItems = ["https://example.com/old.mp4"],
            Write = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        var store = new RecentUrlStore(storage);
        using var cancellation = new CancellationTokenSource();
        var add = store.AddAsync("https://example.com/new.mp4", cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await ((Func<Task>)(() => add)).Should().ThrowAsync<OperationCanceledException>();
        store.Items.Should().Equal("https://example.com/new.mp4", "https://example.com/old.mp4");
        storage.StoredItems.Should().Equal("https://example.com/old.mp4");
        storage.Write = null;
        await store.ClearAsync();

        storage.StoredItems.Should().BeEmpty();
    }

    private sealed class FakeStorage : IRecentUrlStorage
    {
        public IReadOnlyList<string> StoredItems { get; set; } = [];
        public List<IReadOnlyList<string>> WrittenSnapshots { get; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<string>>>? Read { get; set; }
        public Func<IReadOnlyList<string>, CancellationToken, Task>? Write { get; set; }
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(StoredItems);
        }

        public async Task WriteAsync(IReadOnlyList<string> items, CancellationToken cancellationToken)
        {
            WrittenSnapshots.Add(items.ToArray());
            if (Write is not null) await Write(items, cancellationToken);
            StoredItems = items.ToArray();
        }
    }
}
