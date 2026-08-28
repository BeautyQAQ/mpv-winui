namespace MpvShell.Player.LibMpv;

/// <summary>
/// Owns the lifetime of exactly one libmpv core without exposing its native handle.
/// </summary>
public interface IMpvPlayerSession : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
}
