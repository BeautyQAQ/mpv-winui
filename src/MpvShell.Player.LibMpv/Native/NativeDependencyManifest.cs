using System.Text.Json;
using System.Text.Json.Serialization;

namespace MpvShell.Player.LibMpv.Native;

public sealed class NativeDependencyManifest
{
    public int SchemaVersion { get; init; }

    public string Rid { get; init; } = string.Empty;

    public MpvClientApiRequirement ExpectedMpvClientApi { get; init; } = new();

    public IReadOnlyList<NativeDependencyAsset> Assets { get; init; } = [];

    public static NativeDependencyManifest Read(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.ManifestMissing,
                $"原生依赖清单不存在：{Path.GetFullPath(manifestPath)}");
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize<NativeDependencyManifest>(stream, SerializerOptions)
                ?? throw new JsonException("清单内容为空。");
        }
        catch (NativeDependencyException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.ManifestInvalid,
                $"无法读取原生依赖清单：{Path.GetFullPath(manifestPath)}。{ex.Message}",
                ex);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

public sealed class MpvClientApiRequirement
{
    public int Major { get; init; }

    public int MinimumMinor { get; init; }
}

public sealed class NativeDependencyAsset
{
    public string FileName { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string Group { get; init; } = string.Empty;

    public int LoadOrder { get; init; }

    public IReadOnlyList<string> LogicalNames { get; init; } = [];

    public string Sha256 { get; init; } = string.Empty;
}
