using System.Runtime.InteropServices;

namespace MpvShell.Player.LibMpv.Native;

internal sealed class Utf8Arguments : IDisposable
{
    private readonly nint[] _strings;
    internal nint Pointer { get; private set; }

    internal Utf8Arguments(IReadOnlyList<string> arguments)
    {
        _strings = new nint[arguments.Count];
        Pointer = Marshal.AllocHGlobal((arguments.Count + 1) * IntPtr.Size);
        try
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                if (arguments[index].Contains('\0'))
                    throw new ArgumentException("命令参数不能包含空字符。", nameof(arguments));
                _strings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
                Marshal.WriteIntPtr(Pointer, index * IntPtr.Size, _strings[index]);
            }
            Marshal.WriteIntPtr(Pointer, arguments.Count * IntPtr.Size, 0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (var index = 0; index < _strings.Length; index++)
        {
            Marshal.FreeCoTaskMem(_strings[index]);
            _strings[index] = 0;
        }
        if (Pointer != 0)
        {
            Marshal.FreeHGlobal(Pointer);
            Pointer = 0;
        }
    }
}
