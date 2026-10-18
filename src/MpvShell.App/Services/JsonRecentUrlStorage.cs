using System.Text.Json;
using System.Text.Json.Serialization;

namespace MpvShell.App.Services;

public sealed class JsonRecentUrlStorage : IRecentUrlStorage
{
    private const int CurrentVersion = 1;
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private readonly string _path;

    public JsonRecentUrlStorage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("最近打开记录必须使用绝对路径。", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumFileBytes)
                throw new InvalidDataException("最近打开记录超过大小限制。");

            using var content = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (content.Length + count > MaximumFileBytes)
                    throw new InvalidDataException("最近打开记录超过大小限制。");
                content.Write(buffer, 0, count);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var document = JsonSerializer.Deserialize<RecentUrlDocument>(content.GetBuffer().AsSpan(0, (int)content.Length));
            if (document is null || document.Version != CurrentVersion)
                throw new InvalidDataException("最近打开记录版本无效或不受支持。");
            if (document.Items is null)
                throw new InvalidDataException("最近打开记录缺少条目列表。");
            return (IReadOnlyList<string>)document.Items.OfType<string>().ToArray();
        }
        catch (FileNotFoundException) { return Array.Empty<string>(); }
        catch (DirectoryNotFoundException) { return Array.Empty<string>(); }
    }, cancellationToken);

    public Task WriteAsync(IReadOnlyList<string> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, new RecentUrlDocument(CurrentVersion, snapshot),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    // Flush(true) 与最终替换均在后台线程，完成写入后才触碰旧快照。
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                // 不维护可自动恢复的旧备份，避免清空后重新出现已删除历史。
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }, cancellationToken);
    }

    private sealed record RecentUrlDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("items")] string?[]? Items);
}
