// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace MpvShell.Rendering.WinUI.Interop;

/// <summary>
/// Provides interoperation between XAML and a DirectX swap chain.
/// This is the WinUI 3 version of the interface (microsoft.ui.xaml.media.dxinterop.h).
/// </summary>
[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    /// <summary>
    /// Sets the DirectX swap chain for SwapChainPanel.
    /// </summary>
    /// <param name="swapChain">The DirectX swap chain. Pass null to clear.</param>
    /// <returns>HRESULT</returns>
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain);
}
