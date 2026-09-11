using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace MpvShell.App.Diagnostics;

/// <summary>播放和渲染线程只入队；后台每 500 ms 写入并刷新一次，关闭时排空队列。</summary>
internal sealed class SessionTraceListener : TraceListener
{
    private const int QueueCapacity = 8192;
    private readonly Channel<LogEntry> _entries = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource _stop = new();
    private readonly StreamWriter _writer;
    private readonly Task _writerTask;
    private int _closed;
    private long _dropped;

    internal SessionTraceListener(string filePath)
    {
        FilePath = filePath;
        _writer = new StreamWriter(new FileStream(filePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false));
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public override bool IsThreadSafe => true;
    internal string FilePath { get; }
    internal Exception? WriteFailure { get; private set; }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (message is null || Volatile.Read(ref _closed) != 0) return;
        // 单条异常/原生日志也有上限，避免诊断占用无界内存。
        if (message.Length > 32_768) message = message[..32_768] + " [日志过长，已截断]";
        if (!_entries.Writer.TryWrite(new(DateTimeOffset.Now, Environment.CurrentManagedThreadId, message)))
            Interlocked.Increment(ref _dropped);
    }

    public override void Flush()
    {
        if (Volatile.Read(ref _closed) != 0) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // 显式 Flush 仅用于异常/退出；Trace.AutoFlush 必须关闭，不能在逐条消息上等待磁盘。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _entries.Writer.WriteAsync(new(DateTimeOffset.Now, Environment.CurrentManagedThreadId, null, completion),
                timeout.Token).AsTask().GetAwaiter().GetResult();
            completion.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException) { }
    }

    public override void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _entries.Writer.TryComplete();
        _stop.Cancel();
        // 异常的磁盘不能阻止应用退出；后台任务正常情况下会立即排空并关闭文件。
        try
        {
            if (_writerTask.Wait(TimeSpan.FromSeconds(3))) _stop.Dispose();
        }
        catch (AggregateException ex) { WriteFailure ??= ex.GetBaseException(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                    Drain();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            Drain();
        }
        catch (Exception ex)
        {
            WriteFailure = ex;
            _entries.Writer.TryComplete(ex);
        }
        finally
        {
            try { _writer.Dispose(); }
            catch (Exception ex) { WriteFailure ??= ex; }
        }
    }

    private void Drain()
    {
        // 连续输入不能推迟刷新；单次最多处理一个完整队列。
        for (var count = 0; count < QueueCapacity && _entries.Reader.TryRead(out var entry); count++)
        {
            if (entry.Completion is not null)
            {
                _writer.Flush();
                entry.Completion.TrySetResult();
                continue;
            }
            var message = Redact(entry.Message!);
            using var lines = new StringReader(message);
            while (lines.ReadLine() is { } line)
                _writer.WriteLine($"[{entry.Timestamp:O}] [thread {entry.ThreadId}] {line}");
        }
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
            _writer.WriteLine($"[{DateTimeOffset.Now:O}] [logging] 日志队列已满，丢弃 {dropped} 条诊断消息。");
        _writer.Flush();
    }

    internal static string Redact(string message)
    {
        var redacted = Regex.Replace(message, @"https?://[^\s]+", "[媒体地址]", RegexOptions.IgnoreCase);
        // ffmpeg 的 debug 输出可能把 URL 拆成请求行及认证头；这些内容不用于显卡诊断。
        redacted = Regex.Replace(redacted, @"(?im)(\b(?:GET|POST|HEAD)\s+)\S+(\s+HTTP/\d(?:\.\d)?)", "$1[媒体路径]$2");
        return Regex.Replace(redacted, @"(?im)(\b(?:Authorization|Proxy-Authorization|Cookie|Set-Cookie):)[^\r\n]*", "$1 [已隐藏]");
    }

    private readonly record struct LogEntry(DateTimeOffset Timestamp, int ThreadId, string? Message,
        TaskCompletionSource? Completion = null);
}
