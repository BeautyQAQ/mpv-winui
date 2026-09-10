using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MpvShell.Player.LibMpv.Native;

// ABI 对照固定 v0.41.0 include/mpv/{client,render,render_gl}.h；SHA-256 见 source-lock.json。
internal static partial class MpvNative
{
    private const string Library = NativeDependencyResolver.MpvLibraryName;

    [LibraryImport(Library, EntryPoint = "mpv_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint Create();

    [LibraryImport(Library, EntryPoint = "mpv_initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Initialize(nint handle);

    [LibraryImport(Library, EntryPoint = "mpv_terminate_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void TerminateDestroy(nint handle);

    [LibraryImport(Library, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetOptionString(nint handle, string name, string value);

    [LibraryImport(Library, EntryPoint = "mpv_command_async")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int CommandAsync(nint handle, ulong requestId, nint arguments);

    [LibraryImport(Library, EntryPoint = "mpv_get_property_async", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetPropertyAsync(nint handle, ulong requestId, string name, MpvFormat format);

    [LibraryImport(Library, EntryPoint = "mpv_set_property_async", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int SetPropertyAsync(nint handle, ulong requestId, string name, MpvFormat format, nint data);

    [LibraryImport(Library, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ObserveProperty(nint handle, ulong requestId, string name, MpvFormat format);

    [LibraryImport(Library, EntryPoint = "mpv_wait_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint WaitEvent(nint handle, double timeout);

    [LibraryImport(Library, EntryPoint = "mpv_set_wakeup_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void SetWakeupCallback(nint handle, nint callback, nint context);

    [LibraryImport(Library, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int RequestLogMessages(nint handle, string level);

    [LibraryImport(Library, EntryPoint = "mpv_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ErrorString(int error);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int RenderContextCreate(out nint context, nint handle, nint parameters);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_set_update_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void RenderContextSetUpdateCallback(nint context, nint callback, nint callbackContext);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_update")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong RenderContextUpdate(nint context);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_render")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int RenderContextRender(nint context, nint parameters);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_report_swap")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void RenderContextReportSwap(nint context);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void RenderContextFree(nint context);

    internal static void Check(int result, string operation)
    {
        if (result < 0)
            throw new MpvException(operation, result);
    }
}

internal enum MpvFormat
{
    None = 0, String = 1, OsdString = 2, Flag = 3, Int64 = 4, Double = 5,
    Node = 6, NodeArray = 7, NodeMap = 8, ByteArray = 9,
}

internal enum MpvEventId
{
    None = 0, Shutdown = 1, LogMessage = 2, GetPropertyReply = 3,
    SetPropertyReply = 4, CommandReply = 5, StartFile = 6, EndFile = 7,
    FileLoaded = 8, PlaybackRestart = 21, PropertyChange = 22, QueueOverflow = 24,
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    internal MpvEventId Id;
    internal int Error;
    internal ulong ReplyUserData;
    internal nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    internal nint Name;
    internal MpvFormat Format;
    internal nint Data;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct MpvNode
{
    [FieldOffset(0)] internal nint Pointer;
    [FieldOffset(0)] internal long Integer;
    [FieldOffset(0)] internal int Flag;
    [FieldOffset(0)] internal double Number;
    [FieldOffset(8)] internal MpvFormat Format;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvNodeList
{
    internal int Count;
    internal nint Values;
    internal nint Keys;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEndFile
{
    internal int Reason;
    internal int Error;
    internal long PlaylistEntryId;
    internal long PlaylistInsertId;
    internal int PlaylistInsertCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvRenderParameter
{
    internal int Type;
    internal nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvOpenGlInitParameters
{
    internal nint GetProcAddress;
    internal nint Context;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvOpenGlFramebuffer
{
    internal int Framebuffer;
    internal int Width;
    internal int Height;
    internal int InternalFormat;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void MpvWakeupCallback(nint context);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint MpvGetProcAddressCallback(nint context, nint name);
