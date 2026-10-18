namespace MpvShell.App.Services;

public interface IRecentUrlStorage
{
    Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(IReadOnlyList<string> items, CancellationToken cancellationToken);
}
