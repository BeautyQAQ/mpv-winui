using System.Runtime.InteropServices;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Player.LibMpv;

public sealed class MpvException : Exception
{
    public int ErrorCode { get; }

    internal MpvException(string operation, int errorCode)
        : base($"{operation}失败：{Marshal.PtrToStringUTF8(MpvNative.ErrorString(errorCode))}（{errorCode}）。")
    {
        ErrorCode = errorCode;
    }
}
