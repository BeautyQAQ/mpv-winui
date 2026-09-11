using System.Diagnostics;
using Microsoft.Graphics.Display;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using MpvShell.Rendering.WinUI.Interop;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 在窗口的 UI DispatcherQueue 上读取和跟踪 Windows Advanced Color 状态。
/// 创建、Refresh 和 Dispose 必须在同一 UI 线程调用；窗口位置变化时由宿主主动 Refresh。
/// 系统 HDR 设置、SDR 白亮度和色彩能力变化会通过 Changed 通知宿主。
/// </summary>
public sealed class DisplayOutputMonitor : IDisposable
{
    private readonly nint _windowHandle;
    private readonly int _ownerThreadId;
    private readonly DispatcherQueue _dispatcher;
    private DisplayInformation? _displayInformation;
    private bool _disposed;

    public DisplayOutputMonitor(nint windowHandle)
    {
        if (windowHandle == 0)
            throw new ArgumentException("显示器检测需要有效的窗口句柄。", nameof(windowHandle));

        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("显示器检测必须在运行 DispatcherQueue 的 UI 线程创建。");
        _windowHandle = windowHandle;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Refresh();
    }

    public DisplayOutputCapabilities Current { get; private set; } = DisplayOutputCapabilities.Unknown;
    public event Action<DisplayOutputCapabilities>? Changed;

    /// <summary>重新读取系统状态；查询失败时发布未知状态，并保留诊断，避免沿用过期 HDR 状态。</summary>
    public DisplayOutputCapabilities Refresh()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);

        DisplayOutputCapabilities next;
        string displayName = "未知显示器";
        try
        {
            displayName = DisplayMonitorNative.GetDeviceName(_windowHandle);
            if (_displayInformation is null)
            {
                // 官方要求调用线程已有 DispatcherQueue；对象跟踪顶层窗口对应的显示器。
                // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.graphics.display.displayinformation.createforwindowid
                _displayInformation = DisplayInformation.CreateForWindowId(
                    Win32Interop.GetWindowIdFromWindow(_windowHandle));
                _displayInformation.AdvancedColorInfoChanged += OnAdvancedColorInfoChanged;
            }

            var info = _displayInformation.GetAdvancedColorInfo();
            var kind = info.CurrentAdvancedColorKind switch
            {
                DisplayAdvancedColorKind.StandardDynamicRange => DisplayOutputColorKind.StandardDynamicRange,
                DisplayAdvancedColorKind.WideColorGamut => DisplayOutputColorKind.WideColorGamut,
                DisplayAdvancedColorKind.HighDynamicRange => DisplayOutputColorKind.HighDynamicRange,
                _ => DisplayOutputColorKind.Unknown,
            };
            next = DisplayOutputCapabilities.FromSystem(displayName, kind,
                info.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange),
                info.SdrWhiteLevelInNits, info.MaxLuminanceInNits,
                info.MinLuminanceInNits, info.MaxAverageFullFrameLuminanceInNits);
        }
        catch (Exception exception)
        {
            next = DisplayOutputCapabilities.Unknown with
            {
                DisplayName = displayName,
                Diagnostic = $"显示器 Advanced Color 查询失败：{exception.Message}（0x{exception.HResult:X8}）",
            };
        }

        if (Current != next)
        {
            Current = next;
            Changed?.Invoke(next);
        }

        return Current;
    }

    private void OnAdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        // 只排队，避免在 WinRT 回调里重入渲染器或释放事件源。
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { Refresh(); }
            catch (Exception exception)
            {
                Debug.WriteLine($"[DisplayOutputMonitor] 更新显示器状态失败：{exception}");
            }
        });
    }

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("显示器检测只能在创建它的 UI 线程访问。");
    }

    public void Dispose()
    {
        VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        if (_displayInformation is not null)
        {
            _displayInformation.AdvancedColorInfoChanged -= OnAdvancedColorInfoChanged;
            _displayInformation.Dispose();
            _displayInformation = null;
        }
        Changed = null;
    }
}
