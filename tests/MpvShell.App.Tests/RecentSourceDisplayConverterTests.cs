using FluentAssertions;
using MpvShell.App.Views;

namespace MpvShell.App.Tests;

public sealed class RecentSourceDisplayConverterTests
{
    [Theory]
    [InlineData(@"C:\较长的共同目录\电影一.mkv", "电影一.mkv")]
    [InlineData(@"C:\较长的共同目录\电影二.mkv", "电影二.mkv")]
    [InlineData(@"\\server\videos\共享媒体.mp4", "共享媒体.mp4")]
    public void Local_items_should_show_the_file_name_and_keep_the_full_path_in_the_tooltip(string source, string expected)
    {
        RecentSourceDisplayConverter.Format(source).Should().Be(expected);
        RecentSourceDisplayConverter.Format(source, tooltip: true).Should().Be(source);
    }

    [Theory]
    [InlineData("https://media.example/series/episode-1.mkv?token=secret#fragment", "media.example/series/episode-1.mkv", "https://media.example/series/episode-1.mkv")]
    [InlineData("http://user:password@localhost:8080/live/stream.m3u8?key=secret", "localhost:8080/live/stream.m3u8", "http://localhost:8080/live/stream.m3u8")]
    [InlineData("https://media.example/%E4%B8%AD%E6%96%87.mp4", "media.example/中文.mp4", "https://media.example/中文.mp4")]
    public void Network_items_should_show_host_and_path_without_credentials_query_or_fragment(string source, string label, string tooltip)
    {
        RecentSourceDisplayConverter.Format(source).Should().Be(label);
        RecentSourceDisplayConverter.Format(source, tooltip: true).Should().Be(tooltip);
    }

    [Fact]
    public void An_empty_binding_should_have_an_empty_label()
    {
        RecentSourceDisplayConverter.Format(null).Should().BeEmpty();
        RecentSourceDisplayConverter.Format(string.Empty, tooltip: true).Should().BeEmpty();
    }
}
