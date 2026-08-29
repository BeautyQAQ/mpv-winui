using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class NativeDependencyVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mpvshell-native-tests-{Guid.NewGuid():N}");

    public NativeDependencyVerifierTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Verify_accepts_registered_x64_asset_with_matching_hash()
    {
        var assetPath = WritePe("libmpv-2.dll", 0x8664);
        var manifestPath = WriteManifest(Hash(assetPath));

        var result = NativeDependencyVerifier.Verify(
            manifestPath,
            _root,
            verifyProcessArchitecture: false);

        result.AssetPaths["libmpv-2.dll"].Should().Be(assetPath);
    }

    [Fact]
    public void Verify_reports_missing_asset_without_searching_path()
    {
        var manifestPath = WriteManifest(new string('A', 64));

        var act = () => NativeDependencyVerifier.Verify(
            manifestPath,
            _root,
            verifyProcessArchitecture: false);

        act.Should().Throw<NativeDependencyException>()
            .Which.Failure.Should().Be(NativeDependencyFailure.AssetMissing);
    }

    [Fact]
    public void Verify_rejects_non_x64_pe()
    {
        var assetPath = WritePe("libmpv-2.dll", 0x014C);
        var manifestPath = WriteManifest(Hash(assetPath));

        var act = () => NativeDependencyVerifier.Verify(
            manifestPath,
            _root,
            verifyProcessArchitecture: false);

        act.Should().Throw<NativeDependencyException>()
            .Which.Failure.Should().Be(NativeDependencyFailure.AssetArchitectureMismatch);
    }

    [Fact]
    public void Verify_rejects_hash_mismatch()
    {
        WritePe("libmpv-2.dll", 0x8664);
        var manifestPath = WriteManifest(new string('A', 64));

        var act = () => NativeDependencyVerifier.Verify(
            manifestPath,
            _root,
            verifyProcessArchitecture: false);

        act.Should().Throw<NativeDependencyException>()
            .Which.Failure.Should().Be(NativeDependencyFailure.AssetHashMismatch);
    }

    [Fact]
    public void Verify_rejects_placeholder_hash()
    {
        WritePe("libmpv-2.dll", 0x8664);
        var manifestPath = WriteManifest(new string('0', 64));

        var act = () => NativeDependencyVerifier.Verify(
            manifestPath,
            _root,
            verifyProcessArchitecture: false);

        act.Should().Throw<NativeDependencyException>()
            .Which.Failure.Should().Be(NativeDependencyFailure.ManifestInvalid);
    }

    [Theory]
    [InlineData(1, 99)]
    [InlineData(3, 0)]
    [InlineData(2, 4)]
    public void VerifyMpvClientApiVersion_rejects_incompatible_version(int major, int minor)
    {
        var requirement = new MpvClientApiRequirement { Major = 2, MinimumMinor = 5 };
        var version = (uint)((major << 16) | minor);

        var act = () => NativeDependencyVerifier.VerifyMpvClientApiVersion(version, requirement);

        act.Should().Throw<NativeDependencyException>()
            .Which.Failure.Should().Be(NativeDependencyFailure.ClientApiIncompatible);
    }

    [Fact]
    public void VerifyMpvClientApiVersion_accepts_required_version()
    {
        var requirement = new MpvClientApiRequirement { Major = 2, MinimumMinor = 5 };

        var act = () => NativeDependencyVerifier.VerifyMpvClientApiVersion(0x0002_0005, requirement);

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string WritePe(string fileName, ushort machine)
    {
        var bytes = new byte[256];
        BitConverter.GetBytes((ushort)0x5A4D).CopyTo(bytes, 0);
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        BitConverter.GetBytes(0x00004550u).CopyTo(bytes, 0x80);
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);

        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteManifest(string sha256)
    {
        var manifest = new NativeDependencyManifest
        {
            SchemaVersion = 1,
            Rid = "win-x64",
            ExpectedMpvClientApi = new MpvClientApiRequirement
            {
                Major = 2,
                MinimumMinor = 5,
            },
            Assets =
            [
                new NativeDependencyAsset
                {
                    FileName = "libmpv-2.dll",
                    Component = "mpv",
                    Group = "mpv",
                    LoadOrder = 100,
                    LogicalNames = [NativeDependencyResolver.MpvLibraryName],
                    Sha256 = sha256,
                },
            ],
        };

        var path = Path.Combine(_root, "native-dependencies.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
