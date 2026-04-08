using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class RecentUrlStoreTests
{
    [Fact]
    public void Add_should_move_duplicate_url_to_front()
    {
        var store = new RecentUrlStore();

        store.Add("https://a.example/1.m3u8");
        store.Add("https://b.example/2.m3u8");
        store.Add("https://a.example/1.m3u8");

        store.Items[0].Should().Be("https://a.example/1.m3u8");
        store.Items.Should().HaveCount(2);
    }
}
