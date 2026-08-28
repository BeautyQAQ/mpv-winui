using Microsoft.UI.Xaml.Controls;
using MpvShell.Player.LibMpv;

namespace MpvShell.Rendering.WinUI;

public interface IVideoSurfaceRenderer : IAsyncDisposable
{
    /// <summary>
    /// Uses the shared player session without taking ownership of it.
    /// </summary>
    ValueTask InitializeAsync(IMpvPlayerSession session, CancellationToken cancellationToken);
    ValueTask AttachAsync(SwapChainPanel surface, CancellationToken cancellationToken);
    ValueTask ResizeAsync(VideoSurfaceSize size, CancellationToken cancellationToken);
    ValueTask DetachAsync(CancellationToken cancellationToken);
}
