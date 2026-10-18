namespace MpvShell.App.Services;

/// <summary>界面层采集输入与窗口状态，策略本身不依赖 WinUI 或计时器。</summary>
public readonly record struct PlayerChromeContext(
    bool IsReady,
    bool IsWindowActive,
    bool IsPlaying,
    bool IsStopped,
    bool HasOverlay,
    bool HasActiveInteraction,
    bool HasTimelinePreview,
    bool HasTransientUi,
    bool IsPointerOverControls,
    bool HasKeyboardFocusWithinControls);

public enum PlayerEscapeAction
{
    None,
    CancelInteraction,
    CloseTransientUi,
    CloseOverlay,
    ExitFullscreen,
}

public static class PlayerChromePolicy
{
    // 控制条双行布局所需的窗口下限；由 MainWindow 应用到 OverlappedPresenter。
    public const int MinimumWindowWidth = 720;
    public const int MinimumWindowHeight = 640;

    public static bool CanAutoHide(PlayerChromeContext context) =>
        context.IsReady && context.IsWindowActive && context.IsPlaying && !context.IsStopped &&
        !context.HasOverlay && !context.HasActiveInteraction && !context.HasTimelinePreview &&
        !context.HasTransientUi && !context.IsPointerOverControls && !context.HasKeyboardFocusWithinControls;

    /// <summary>每次 Esc 只关闭最上层的活动输入或界面，最后才退出全屏。</summary>
    public static PlayerEscapeAction ResolveEscape(
        bool hasActiveInteraction,
        bool hasTimelinePreview,
        bool hasTransientUi,
        bool hasOverlay,
        bool isFullscreen)
    {
        if (hasActiveInteraction || hasTimelinePreview) return PlayerEscapeAction.CancelInteraction;
        if (hasTransientUi) return PlayerEscapeAction.CloseTransientUi;
        if (hasOverlay) return PlayerEscapeAction.CloseOverlay;
        return isFullscreen ? PlayerEscapeAction.ExitFullscreen : PlayerEscapeAction.None;
    }
}
