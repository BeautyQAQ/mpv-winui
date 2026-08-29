namespace MpvShell.Player.LibMpv.Native;

public sealed class NativeDependencyException : Exception
{
    public NativeDependencyException(
        NativeDependencyFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public NativeDependencyFailure Failure { get; }
}
