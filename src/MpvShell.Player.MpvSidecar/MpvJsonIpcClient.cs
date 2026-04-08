using System.IO.Pipes;
using System.Text;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvJsonIpcClient : IAsyncDisposable
{
    private NamedPipeClientStream? _pipe;

    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(cancellationToken);
    }

    public async Task SendAsync(string commandJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_pipe);

        var payload = Encoding.UTF8.GetBytes(commandJson + "\n");
        await _pipe.WriteAsync(payload, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _pipe?.Dispose();
        return ValueTask.CompletedTask;
    }
}
