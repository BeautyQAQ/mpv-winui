using FluentAssertions;
using MpvShell.App.Diagnostics;

namespace MpvShell.App.Tests;

public sealed class SessionTraceListenerTests
{
    [Fact]
    public void Closing_should_flush_queued_multiline_logs_with_timestamp_thread_and_url_redaction()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mpvshell-log-test-{Guid.NewGuid():N}.log");
        try
        {
            using var listener = new SessionTraceListener(file);
            var threadId = Environment.CurrentManagedThreadId;
            listener.WriteLine("first\r\nGET /video?token=secret HTTP/1.1\r\nAuthorization: Bearer credentials\r\nhttps://example.com/path?key=private");
            listener.Close();
            var lines = File.ReadAllLines(file);
            lines.Should().HaveCount(4);
            foreach (var line in lines)
            {
                DateTimeOffset.TryParse(line[1..line.IndexOf(']')], out _).Should().BeTrue();
                line.Should().Contain($"[thread {threadId}]");
            }
            var contents = string.Join('\n', lines);
            contents.Should().Contain("first").And.Contain("[媒体地址]").And.Contain("[媒体路径]");
            contents.Should().NotContain("secret").And.NotContain("credentials").And.NotContain("private");
            listener.WriteFailure.Should().BeNull();
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Explicit_flush_should_make_prior_messages_readable_while_listener_remains_open()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mpvshell-log-test-{Guid.NewGuid():N}.log");
        try
        {
            using var listener = new SessionTraceListener(file);
            listener.WriteLine("before flush");
            listener.Flush();
            using (var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                reader.ReadToEnd().Should().Contain("before flush");
            listener.WriteLine("after flush");
            listener.Close();
            File.ReadAllText(file).Should().Contain("after flush");
            listener.WriteFailure.Should().BeNull();
        }
        finally { File.Delete(file); }
    }
}
