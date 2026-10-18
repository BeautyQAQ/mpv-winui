using System.Text;
using System.Text.Json;
using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class JsonRecentUrlStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MpvShell-history-tests", Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_directory, "recent-urls.json");

    [Fact]
    public async Task Missing_file_and_parent_directory_should_restore_empty_history_without_creating_files()
    {
        var store = CreateStore();

        await store.InitializeAsync();

        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().BeNull();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task Restart_should_restore_order_deduplication_and_limit_with_minimal_versioned_json()
    {
        var store = CreateStore();
        for (var index = 0; index < 12; index++)
            await store.AddAsync($"https://example.com/{index}.mp4");
        await store.AddAsync("https://example.com/8.mp4");
        var restarted = CreateStore();

        await restarted.InitializeAsync();

        restarted.Items.Should().Equal(store.Items);
        restarted.Items.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        restarted.Items[0].Should().Be("https://example.com/8.mp4");
        var bytes = await File.ReadAllBytesAsync(HistoryPath);
        bytes.Take(3).Should().NotEqual([0xef, 0xbb, 0xbf]);
        using var document = JsonDocument.Parse(bytes);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("version", "items");
        document.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        Directory.GetFiles(_directory).Should().Equal(HistoryPath);
    }

    [Fact]
    public async Task Unicode_local_path_and_full_http_address_should_round_trip_without_requiring_file_existence()
    {
        var path = Path.Combine(_directory, "不存在的电影 🎞.mkv");
        var url = "https://example.com/视频.mp4?token=preserve-for-reopen#part";
        var store = CreateStore();
        await store.AddAsync(path);
        await store.AddAsync(url);
        var restarted = CreateStore();

        await restarted.InitializeAsync();

        restarted.Items.Should().Equal(url, path);
    }

    [Fact]
    public async Task Ten_maximum_length_unicode_addresses_should_fit_the_file_limit_and_round_trip()
    {
        var store = CreateStore();
        for (var index = 0; index < 10; index++)
        {
            var prefix = $"https://example.com/{index}/";
            await store.AddAsync(prefix + new string('中', 32768 - prefix.Length));
        }
        var restarted = CreateStore();

        await restarted.InitializeAsync();

        restarted.Items.Should().HaveCount(10).And.Equal(store.Items);
        restarted.PersistenceError.Should().BeNull();
        new FileInfo(HistoryPath).Length.Should().BeGreaterThan(1024 * 1024).And.BeLessThan(4 * 1024 * 1024);
    }

    [Fact]
    public async Task Clear_should_survive_restart_and_ignore_unrelated_old_backup()
    {
        var store = CreateStore();
        await store.AddAsync("https://example.com/old.mp4");
        File.Copy(HistoryPath, HistoryPath + ".bak");

        await store.ClearAsync();
        var restarted = CreateStore();
        await restarted.InitializeAsync();

        restarted.Items.Should().BeEmpty();
        restarted.PersistenceError.Should().BeNull();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(HistoryPath));
        document.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"version\":99,\"items\":[]}")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"items\":null}")]
    [InlineData("{\"version\":1,\"items\":[123]}")]
    public async Task Invalid_document_should_not_prevent_startup_or_subsequent_successful_save(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(HistoryPath, json, new UTF8Encoding(false));
        var store = CreateStore();

        await store.InitializeAsync();

        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().Contain("无法读取");
        (await File.ReadAllTextAsync(HistoryPath)).Should().Be(json);
        await store.AddAsync("https://example.com/new.mp4");
        store.PersistenceError.Should().BeNull();
        var restarted = CreateStore();
        await restarted.InitializeAsync();
        restarted.Items.Should().Equal("https://example.com/new.mp4");
    }

    [Fact]
    public async Task Oversized_document_should_be_rejected_without_reading_it_into_history()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(HistoryPath, new string(' ', 4 * 1024 * 1024 + 1), new UTF8Encoding(false));
        var store = CreateStore();

        await store.InitializeAsync();

        store.Items.Should().BeEmpty();
        store.PersistenceError.Should().Contain("无法读取");
    }

    [Fact]
    public async Task Unwritable_destination_should_keep_session_usable_and_report_failed_save()
    {
        Directory.CreateDirectory(HistoryPath);
        var store = CreateStore();

        await store.InitializeAsync();
        await store.AddAsync("https://example.com/new.mp4");

        store.Items.Should().Equal("https://example.com/new.mp4");
        store.PersistenceError.Should().Contain("未能保存");
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_atomic_replacement_should_preserve_old_file_and_remove_temporary_file()
    {
        var storage = new JsonRecentUrlStorage(HistoryPath);
        await storage.WriteAsync(["https://example.com/old.mp4"], CancellationToken.None);
        var original = await File.ReadAllBytesAsync(HistoryPath);
        using (var heldFile = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var write = () => storage.WriteAsync(["https://example.com/new.mp4"], CancellationToken.None);

            var failure = await write.Should().ThrowAsync<Exception>();
            (failure.Which is IOException or UnauthorizedAccessException).Should().BeTrue("锁定目标的替换失败应为文件访问错误");

            (await File.ReadAllBytesAsync(HistoryPath)).Should().Equal(original);
            Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
        }

        (await storage.ReadAsync(CancellationToken.None)).Should().Equal("https://example.com/old.mp4");
        await storage.WriteAsync([], CancellationToken.None);
        (await storage.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_storage_operations_should_not_create_or_replace_files()
    {
        var storage = new JsonRecentUrlStorage(HistoryPath);
        await storage.WriteAsync(["https://example.com/old.mp4"], CancellationToken.None);
        var original = await File.ReadAllBytesAsync(HistoryPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await ((Func<Task>)(() => storage.ReadAsync(cancellation.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => storage.WriteAsync([], cancellation.Token))).Should().ThrowAsync<OperationCanceledException>();

        (await File.ReadAllBytesAsync(HistoryPath)).Should().Equal(original);
        Directory.GetFiles(_directory).Should().Equal(HistoryPath);
    }

    private RecentUrlStore CreateStore() => new(new JsonRecentUrlStorage(HistoryPath));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
