#if DEBUG
namespace MpvShell.Rendering.WinUI;

public sealed partial class D3D11VideoSurfaceRenderer
{
    // 仅应用内验收使用；Release 不包含故障注入或 CPU 像素读回入口。
    private int _testGraphicsGeneration;
    private Action<int>? _testBeforeRender;
    private Action<D3D11DeviceManager, CompositionSwapChain, int>? _testReadFrame;
    private Action? _testPresented;
    private Action? _testBeforeCreate;
    private Action<int>? _testBeforeResize;

    internal Task SetTestCallbacksAsync(Action<int>? beforeRender,
        Action<D3D11DeviceManager, CompositionSwapChain, int>? readFrame, Action? presented,
        Action? beforeCreate = null, Action<int>? beforeResize = null) =>
        _worker!.InvokeAsync(() =>
        {
            _testBeforeRender = beforeRender;
            _testReadFrame = readFrame;
            _testPresented = presented;
            _testBeforeCreate = beforeCreate;
            _testBeforeResize = beforeResize;
        });

    internal Task RequestTestFrameAsync() => _worker!.InvokeAsync(() => _forceRedraw = true);
}
#endif
