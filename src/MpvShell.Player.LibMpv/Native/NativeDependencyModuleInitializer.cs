using System.Runtime.CompilerServices;

namespace MpvShell.Player.LibMpv.Native;

internal static class NativeDependencyModuleInitializer
{
#pragma warning disable CA2255 // The library intentionally owns deterministic resolution for its P/Invokes.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize() => NativeDependencyResolver.Register();
}
