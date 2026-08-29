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

    [Fact]
    public void Surface_size_should_convert_to_physical_pixels_using_scale()
    {
        var size = new VideoSurfaceSize(1280, 720, 1.5);

        size.PhysicalWidth.Should().Be(1920);
        size.PhysicalHeight.Should().Be(1080);
    }

    [Fact]
    public void Surface_size_physical_pixels_should_round_and_never_be_zero()
    {
        var scaled = new VideoSurfaceSize(10, 20, 0.25); // 2.5 -> 2 (MidpointRounding.ToEven); 5 -> 5
        scaled.PhysicalWidth.Should().Be(2);
        scaled.PhysicalHeight.Should().Be(5);

        var tiny = new VideoSurfaceSize(0.2, 0.4, 1.0); // 低于 1，向上取整为 1
        tiny.PhysicalWidth.Should().Be(1);
        tiny.PhysicalHeight.Should().Be(1);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2400, 1350, 1.25)]
    [InlineData(3840, 2160, 2.0)]
    public void Surface_size_dpi_scaling_matrix(double expectedW, double expectedH, double scale)
    {
        var size = new VideoSurfaceSize(1920, 1080, scale);

        size.PhysicalWidth.Should().Be((uint)expectedW);
        size.PhysicalHeight.Should().Be((uint)expectedH);
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
