namespace MpvShell.Player.LibMpv.Native;

public enum NativeDependencyFailure
{
    UnsupportedProcessArchitecture,
    ManifestMissing,
    ManifestInvalid,
    AssetMissing,
    AssetArchitectureMismatch,
    AssetHashMismatch,
    LoadFailed,
    ExportMissing,
    ClientApiIncompatible,
}
