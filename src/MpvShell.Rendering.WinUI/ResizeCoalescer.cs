// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 合并连续 resize 请求：仅当物理像素尺寸变化时才要求重新调整 SwapChain。
/// </summary>
public sealed class ResizeCoalescer
{
    private uint _lastWidth;
    private uint _lastHeight;
    private bool _hasLast;

    /// <summary>
    /// 将新的物理尺寸与上一次相比，若变化则记录并返回 true，否则返回 false。
    /// </summary>
    public bool ShouldResize(uint physicalWidth, uint physicalHeight)
    {
        if (_hasLast && _lastWidth == physicalWidth && _lastHeight == physicalHeight)
        {
            return false;
        }

        _lastWidth = physicalWidth;
        _lastHeight = physicalHeight;
        _hasLast = true;
        return true;
    }

    /// <summary>
    /// 重置状态（例如重新绑定表面时）。
    /// </summary>
    public void Reset()
    {
        _hasLast = false;
        _lastWidth = 0;
        _lastHeight = 0;
    }
}