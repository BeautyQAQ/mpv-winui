using System.Diagnostics;
using System.Security;
using System.Text.Json;

namespace MpvShell.App.Services;

public sealed class RecentUrlStore
{
    private const int MaximumItems = 10;
    private const int MaximumSourceLength = 32768;
    private readonly IRecentUrlStorage? _storage;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<string> _items = Array.Empty<string>();
    private bool _initialized;

    // 无参实例用于纯内存场景；生产实例显式传入用户目录中的持久化服务。
    public RecentUrlStore() { }

    public RecentUrlStore(IRecentUrlStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    public IReadOnlyList<string> Items => _items;

    public string? PersistenceError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await InitializeCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task AddAsync(string url, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValidSource(url)) return;

            var updated = new List<string>(MaximumItems) { url };
            updated.AddRange(_items.Where(item => !StringComparer.Ordinal.Equals(item, url)).Take(MaximumItems - 1));
            _items = updated.AsReadOnly();
            await PersistCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _items = Array.Empty<string>();
            await PersistCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        try
        {
            if (_storage is not null)
            {
                var loaded = await _storage.ReadAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _items = Array.AsReadOnly(loaded.Where(IsValidSource).Distinct(StringComparer.Ordinal).Take(MaximumItems).ToArray());
            }
            PersistenceError = null;
            _initialized = true;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            ReportPersistenceFailure("无法读取最近打开记录，本次仍可正常打开媒体。", exception);
            _initialized = true;
        }
    }

    private async Task PersistCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_storage is not null)
                await _storage.WriteAsync(_items, cancellationToken).ConfigureAwait(false);
            PersistenceError = null;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            ReportPersistenceFailure("最近打开记录未能保存，重启后可能恢复此前记录。", exception);
        }
    }

    private void ReportPersistenceFailure(string message, Exception exception)
    {
        PersistenceError = message;
        // 异常消息可能含路径、地址或原始 JSON，只记录异常类型。
        Trace.WriteLine($"[最近打开] {message} 错误类型：{exception.GetType().Name}");
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or InvalidDataException;

    private static bool IsValidSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumSourceLength || source.IndexOf('\0') >= 0) return false;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host))
            return true;

        // 历史条目无需当前存在；真正打开时仍由播放后端校验媒体。
        return Path.IsPathFullyQualified(source) && source.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }
}
