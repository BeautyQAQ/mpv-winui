using FluentAssertions;
using MpvShell.Interop.VideoHost;

namespace MpvShell.Interop.VideoHost.Tests;

public sealed class HostBoundsTranslatorTests
{
    [Fact]
    public void Should_translate_logical_size_to_pixel_bounds()
    {
        var rect = HostBoundsTranslator.Translate(0, 0, 800, 450, 1.5);

        rect.Width.Should().Be(1200);
        rect.Height.Should().Be(675);
    }
}
