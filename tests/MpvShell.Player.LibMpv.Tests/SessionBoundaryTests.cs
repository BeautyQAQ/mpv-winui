using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class SessionBoundaryTests
{
    [Fact]
    public void Session_contract_should_not_expose_native_handles()
    {
        var exposedTypes = typeof(IMpvPlayerSession)
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        exposedTypes.Should().NotContain(typeof(nint));
    }

    [Fact]
    public void LibMpv_project_should_not_reference_legacy_projects()
    {
        var references = typeof(IMpvPlayerSession).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        references.Should().NotContain("MpvShell.Player.MpvSidecar");
        references.Should().NotContain("MpvShell.Interop.VideoHost");
    }
}
