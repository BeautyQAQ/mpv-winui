using System.Reflection;
using System.Runtime.InteropServices;

namespace MpvShell.Player.LibMpv.Native;

public static class NativeDependencyResolver
{
    public const string MpvLibraryName = "mpv";
    public const string EglLibraryName = "EGL";
    public const string GlesLibraryName = "GLESv2";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, nint> Handles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Assembly> RegisteredAssemblies = [];
    private static NativeDependencyVerificationResult? _verification;

    public static void Register() => RegisterForAssembly(typeof(NativeDependencyResolver).Assembly);

    public static void RegisterForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        lock (Gate)
        {
            if (RegisteredAssemblies.Contains(assembly))
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);
            RegisteredAssemblies.Add(assembly);
        }
    }

    private static nint ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!IsManagedLogicalName(libraryName))
        {
            return nint.Zero;
        }

        lock (Gate)
        {
            var verification = _verification ??= VerifyDefaultLayout();
            var target = verification.Manifest.Assets.SingleOrDefault(
                asset => asset.LogicalNames.Contains(libraryName, StringComparer.OrdinalIgnoreCase));

            if (target is null)
            {
                throw new NativeDependencyException(
                    NativeDependencyFailure.ManifestInvalid,
                    $"逻辑库名 {libraryName} 未在原生依赖清单中登记。");
            }

            LoadGroup(verification, target.Group);
            var handle = Handles[target.FileName];

            if (string.Equals(target.Group, "mpv", StringComparison.OrdinalIgnoreCase))
            {
                VerifyMpvClientApi(handle, verification.Manifest.ExpectedMpvClientApi);
            }

            return handle;
        }
    }

    private static NativeDependencyVerificationResult VerifyDefaultLayout()
    {
        var nativeDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            "win-x64",
            "native");
        var manifestPath = Path.Combine(nativeDirectory, "native-dependencies.json");
        return NativeDependencyVerifier.Verify(manifestPath, nativeDirectory);
    }

    private static void LoadGroup(
        NativeDependencyVerificationResult verification,
        string group)
    {
        foreach (var asset in verification.Manifest.Assets
                     .Where(asset => string.Equals(asset.Group, group, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(asset => asset.LoadOrder))
        {
            if (Handles.ContainsKey(asset.FileName))
            {
                continue;
            }

            try
            {
                Handles.Add(asset.FileName, NativeLibrary.Load(verification.AssetPaths[asset.FileName]));
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                throw new NativeDependencyException(
                    NativeDependencyFailure.LoadFailed,
                    $"加载 {asset.FileName} 失败；文件本身已通过架构和哈希校验，" +
                    "请检查清单是否遗漏非系统依赖。" + $" 原始错误：{ex.Message}",
                    ex);
            }
        }
    }

    private static void VerifyMpvClientApi(nint handle, MpvClientApiRequirement requirement)
    {
        if (!NativeLibrary.TryGetExport(handle, "mpv_client_api_version", out var export))
        {
            throw new NativeDependencyException(
                NativeDependencyFailure.ExportMissing,
                "libmpv-2.dll 未导出 mpv_client_api_version。");
        }

        var getVersion = Marshal.GetDelegateForFunctionPointer<MpvClientApiVersion>(export);
        var version = getVersion();
        NativeDependencyVerifier.VerifyMpvClientApiVersion(version, requirement);
    }

    private static bool IsManagedLogicalName(string libraryName) =>
        string.Equals(libraryName, MpvLibraryName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(libraryName, EglLibraryName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(libraryName, GlesLibraryName, StringComparison.OrdinalIgnoreCase);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint MpvClientApiVersion();
}
