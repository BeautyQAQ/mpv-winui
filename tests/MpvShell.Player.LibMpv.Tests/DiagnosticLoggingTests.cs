using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class DiagnosticLoggingTests
{
    [Theory]
    [InlineData(null, "error")]
    [InlineData("", "error")]
    [InlineData("unsupported", "error")]
    [InlineData(" DEBUG ", "debug")]
    [InlineData("warning", "warn")]
    [InlineData("verbose", "v")]
    [InlineData("trace", "trace")]
    public void Log_level_should_preserve_safe_default_and_accept_diagnostic_override(string? requested, string expected)
    {
        MpvPlayerSession.NormalizeLogLevel(requested).Should().Be(expected);
    }

    [Fact]
    public void Native_logs_should_keep_component_and_level_without_exposing_signed_urls()
    {
        var message = MpvPlayerSession.FormatLogMessage("ffmpeg", "debug",
            "Opening https://user:password@media.example/video?token=secret and HTTP://other.example/path\r\n");
        message.Should().StartWith("[libmpv/ffmpeg/debug]");
        message.Should().Contain("[媒体地址]");
        message.Should().NotContain("secret").And.NotContain("password").And.NotContain("media.example");
        message.Should().NotEndWith("\n");
    }
}
