using FluentAssertions;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class LegacyMpvHostTests
{
    [Fact]
    public void Should_return_attached_handle()
    {
        var host = new LegacyMpvHost();

        host.Attach((nint)1234);

        host.GetRequiredHandle().Should().Be((nint)1234);
    }

    [Fact]
    public void Should_reject_missing_handle()
    {
        var host = new LegacyMpvHost();

        var act = host.GetRequiredHandle;

        act.Should().Throw<InvalidOperationException>();
    }
}
