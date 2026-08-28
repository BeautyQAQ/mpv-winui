using FluentAssertions;

namespace MpvShell.Player.Abstractions.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Abstractions_should_not_reference_platform_or_implementation_assemblies()
    {
        var references = typeof(IPlayerBackend).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        var forbiddenReferences = references.Where(reference =>
            reference != null &&
            (reference.StartsWith("Microsoft.UI", StringComparison.Ordinal) ||
             reference.Contains("D3D", StringComparison.OrdinalIgnoreCase) ||
             reference.Contains("ANGLE", StringComparison.OrdinalIgnoreCase) ||
             reference.Contains("LibMpv", StringComparison.OrdinalIgnoreCase)));

        forbiddenReferences.Should().BeEmpty();
    }
}
