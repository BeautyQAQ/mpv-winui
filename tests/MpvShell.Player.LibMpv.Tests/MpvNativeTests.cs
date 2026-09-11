using System.Runtime.InteropServices;
using FluentAssertions;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class MpvNativeTests
{
    [Fact]
    public void Render_release_should_attempt_free_even_when_callback_unregistration_fails()
    {
        var unregisterFailure = new EntryPointNotFoundException("测试注销失败");
        var freed = false;
        var result = MpvRenderContext.ReleaseNativeResources(() => throw unregisterFailure, () => freed = true);
        freed.Should().BeTrue();
        result.Should().BeSameAs(unregisterFailure);
    }

    [Fact]
    public void Render_release_should_preserve_both_failures_without_reporting_a_successful_free()
    {
        var unregisterFailure = new EntryPointNotFoundException("测试注销失败");
        var freeFailure = new EntryPointNotFoundException("测试释放失败");
        var action = () => MpvRenderContext.ReleaseNativeResources(() => throw unregisterFailure, () => throw freeFailure);
        action.Should().Throw<AggregateException>().Which.InnerExceptions.Should().ContainInOrder(unregisterFailure, freeFailure);
    }

    [Fact]
    public void X64_layout_should_match_locked_client_and_render_headers()
    {
        IntPtr.Size.Should().Be(8);
        Marshal.SizeOf<MpvEvent>().Should().Be(24);
        Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.ReplyUserData)).ToInt32().Should().Be(8);
        Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.Data)).ToInt32().Should().Be(16);
        Marshal.SizeOf<MpvEventProperty>().Should().Be(24);
        Marshal.OffsetOf<MpvEventProperty>(nameof(MpvEventProperty.Data)).ToInt32().Should().Be(16);
        Marshal.SizeOf<MpvNode>().Should().Be(16);
        Marshal.OffsetOf<MpvNode>(nameof(MpvNode.Format)).ToInt32().Should().Be(8);
        Marshal.SizeOf<MpvNodeList>().Should().Be(24);
        Marshal.SizeOf<MpvEndFile>().Should().Be(32);
        Marshal.SizeOf<MpvRenderParameter>().Should().Be(16);
        Marshal.SizeOf<MpvOpenGlInitParameters>().Should().Be(16);
        Marshal.SizeOf<MpvOpenGlFramebuffer>().Should().Be(16);
        ((int)MpvFormat.Node).Should().Be(6);
        ((int)MpvFormat.NodeMap).Should().Be(8);
        ((int)MpvEventId.PropertyChange).Should().Be(22);
        ((int)MpvEventId.CommandReply).Should().Be(5);
    }

    [Fact]
    public void Command_arguments_should_preserve_utf8_and_argument_boundaries()
    {
        using var arguments = new Utf8Arguments(["loadfile", "C:\\视频\\空 格;测试.mp4", "replace"]);
        Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(arguments.Pointer, 8)).Should().Be("C:\\视频\\空 格;测试.mp4");
        Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(arguments.Pointer, 16)).Should().Be("replace");
        Marshal.ReadIntPtr(arguments.Pointer, 24).Should().Be(0);
        arguments.Dispose();
        arguments.Pointer.Should().Be(0);
    }

    [Fact]
    public void Command_arguments_should_reject_embedded_nulls()
    {
        var action = () => new Utf8Arguments(["loadfile", "test\0.mp4"]);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Property_mapping_should_parse_tracks_and_media_info()
    {
        object?[] tracks =
        [
            new Dictionary<string, object?> { ["id"] = 1L, ["type"] = "audio", ["lang"] = "zho", ["selected"] = true },
            new Dictionary<string, object?> { ["id"] = 2L, ["type"] = "sub", ["title"] = "中文字幕", ["selected"] = false },
            new Dictionary<string, object?> { ["id"] = 1L, ["type"] = "video" },
        ];
        var mapped = LibMpvBackend.ParseTracks(tracks);
        mapped.Should().HaveCount(3);
        mapped[0].Language.Should().Be("zho");
        mapped[1].Title.Should().Be("中文字幕");
        mapped[2].Id.Should().Be(0);
        mapped[2].Selected.Should().BeTrue();
        var info = LibMpvBackend.BuildInfo(new Dictionary<string, object?>
        {
            ["video-params"] = new Dictionary<string, object?> { ["w"] = 1280L, ["h"] = 720L, ["gamma"] = "bt.1886", ["pixelformat"] = "yuv420p" },
            ["video-codec"] = "h264", ["audio-codec-name"] = "aac", ["container-fps"] = 29.97,
        });
        info.Resolution.Should().Be("1280 × 720");
        info.HdrType.Should().Be("SDR");
        info.FrameRate.Should().Be("29.97 fps");
    }

    [Theory]
    [InlineData("https://example.com/视频.mp4?token=abc")]
    [InlineData("http://127.0.0.1:8000/test.m3u8")]
    public void Media_sources_should_allow_http_and_https(string source)
    {
        LibMpvBackend.ValidateMediaSource(source).Should().StartWith("http");
    }

    [Theory]
    [InlineData("ftp://example.com/media")]
    [InlineData("av://lavfi:testsrc")]
    [InlineData("memory://data")]
    [InlineData("cmd.exe /c echo hello")]
    [InlineData("https://example.com/a\0b")]
    public void Media_sources_should_reject_other_protocols_and_commands(string source)
    {
        var action = () => LibMpvBackend.ValidateMediaSource(source);
        action.Should().Throw<ArgumentException>();
    }
}
