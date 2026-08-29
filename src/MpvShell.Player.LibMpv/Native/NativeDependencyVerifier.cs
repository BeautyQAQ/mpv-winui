using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MpvShell.Player.LibMpv.Native;

public sealed class NativeDependencyVerificationResult
{
    internal NativeDependencyVerificationResult(
        NativeDependencyManifest manifest,
        IReadOnlyDictionary<string, string> assetPaths)
    {
        Manifest = manifest;
        AssetPaths = assetPaths;
    }

    public NativeDependencyManifest Manifest { get; }

    public IReadOnlyDictionary<string, string> AssetPaths { get; }
}

public static class NativeDependencyVerifier
{
    private const ushort DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort Amd64Machine = 0x8664;

    public static NativeDependencyVerificationResult Verify(
        string manifestPath,
        string nativeDirectory,
        bool verifyProcessArchitecture = true)
    {
        if (verifyProcessArchitecture &&
            (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.UnsupportedProcessArchitecture,
                $"原生依赖仅支持 x64 进程；当前进程架构为 {RuntimeInformation.ProcessArchitecture}。");
        }

        var manifest = NativeDependencyManifest.Read(manifestPath);
        ValidateManifest(manifest);

        var root = Path.GetFullPath(nativeDirectory);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in manifest.Assets)
        {
            var path = ResolveAssetPath(root, asset.FileName);

            if (!File.Exists(path))
            {
                throw new NativeDependencyException(
                    NativeDependencyFailure.AssetMissing,
                    $"缺少已登记的原生依赖：{asset.FileName}。预期路径：{path}");
            }

            VerifyPeX64(path, asset.FileName);
            VerifyHash(path, asset);
            paths.Add(asset.FileName, path);
        }

        return new NativeDependencyVerificationResult(manifest, paths);
    }

    public static void VerifyMpvClientApiVersion(
        uint version,
        MpvClientApiRequirement requirement)
    {
        var major = (int)(version >> 16);
        var minor = (int)(version & 0xFFFF);

        if (major != requirement.Major || minor < requirement.MinimumMinor)
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.ClientApiIncompatible,
                $"libmpv Client API {major}.{minor} 与要求的 " +
                $"{requirement.Major}.{requirement.MinimumMinor}+ 不兼容。");
        }
    }

    private static void ValidateManifest(NativeDependencyManifest manifest)
    {
        if (manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.Rid, "win-x64", StringComparison.Ordinal) ||
            manifest.ExpectedMpvClientApi.Major <= 0 ||
            manifest.ExpectedMpvClientApi.MinimumMinor < 0 ||
            manifest.Assets.Count == 0)
        {
            throw InvalidManifest("schemaVersion、RID、Client API 或资产列表无效。");
        }

        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in manifest.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.FileName) ||
                string.IsNullOrWhiteSpace(asset.Component) ||
                string.IsNullOrWhiteSpace(asset.Group) ||
                !fileNames.Add(asset.FileName))
            {
                throw InvalidManifest($"资产登记无效或文件名重复：{asset.FileName}");
            }

            if (asset.Sha256.Length != 64 ||
                asset.Sha256.All(character => character == '0') ||
                !asset.Sha256.All(Uri.IsHexDigit))
            {
                throw InvalidManifest($"{asset.FileName} 的 SHA-256 尚未填写或格式无效。");
            }

            foreach (var logicalName in asset.LogicalNames)
            {
                if (string.IsNullOrWhiteSpace(logicalName) || !logicalNames.Add(logicalName))
                {
                    throw InvalidManifest($"逻辑库名为空或重复：{logicalName}");
                }
            }
        }

        if (!logicalNames.Contains(NativeDependencyResolver.MpvLibraryName))
        {
            throw InvalidManifest($"清单必须登记逻辑库名 {NativeDependencyResolver.MpvLibraryName}。");
        }
    }

    private static string ResolveAssetPath(string root, string fileName)
    {
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            Path.IsPathRooted(fileName))
        {
            throw InvalidManifest($"资产文件名必须是简单文件名：{fileName}");
        }

        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidManifest($"资产路径越过固定 RID 目录：{fileName}");
        }

        return path;
    }

    private static void VerifyPeX64(string path, string fileName)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 64 || reader.ReadUInt16() != DosSignature)
            {
                throw ArchitectureMismatch(fileName, "不是有效的 PE 文件");
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > stream.Length - 6)
            {
                throw ArchitectureMismatch(fileName, "PE 头偏移无效");
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != PeSignature)
            {
                throw ArchitectureMismatch(fileName, "PE 签名无效");
            }

            var machine = reader.ReadUInt16();
            if (machine != Amd64Machine)
            {
                throw ArchitectureMismatch(fileName, $"Machine=0x{machine:X4}，预期 AMD64(0x8664)");
            }
        }
        catch (NativeDependencyException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.AssetArchitectureMismatch,
                $"无法检查 {fileName} 的 PE 架构：{ex.Message}",
                ex);
        }
    }

    private static void VerifyHash(string path, NativeDependencyAsset asset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(SHA256.HashData(stream));

        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.AssetHashMismatch,
                $"{asset.FileName} 的 SHA-256 不匹配。预期 {asset.Sha256.ToUpperInvariant()}，实际 {actual}。");
        }
    }

    private static NativeDependencyException ArchitectureMismatch(string fileName, string detail) =>
        new(
            NativeDependencyFailure.AssetArchitectureMismatch,
            $"{fileName} 不是已登记的 x64 PE：{detail}。");

    private static NativeDependencyException InvalidManifest(string detail) =>
        new(NativeDependencyFailure.ManifestInvalid, $"原生依赖清单无效：{detail}");
}
