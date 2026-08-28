using FluentAssertions;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class VideoSurfaceContractTests
{
    [Fact]
    public void Surface_size_should_preserve_logical_dimensions_and_scale()
    {
        var size = new VideoSurfaceSize(1280, 720, 1.5);

        size.LogicalWidth.Should().Be(1280);
        size.LogicalHeight.Should().Be(720);
        size.RasterizationScale.Should().Be(1.5);
    }

    [Theory]
    [InlineData(-1, 720, 1)]
    [InlineData(1280, -1, 1)]
    [InlineData(1280, 720, 0)]
    [InlineData(1280, 720, -1)]
    public void Surface_size_should_reject_invalid_values(double width, double height, double scale)
    {
        var act = () => new VideoSurfaceSize(width, height, scale);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Renderer_contract_should_not_expose_native_handles()
    {
        var exposedTypes = typeof(IVideoSurfaceRenderer)
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        exposedTypes.Should().NotContain(typeof(nint));
    }

    [Fact]
    public void Rendering_project_should_not_reference_legacy_projects()
    {
        var references = typeof(IVideoSurfaceRenderer).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        references.Should().NotContain("MpvShell.Player.MpvSidecar");
        references.Should().NotContain("MpvShell.Interop.VideoHost");
    }
}
