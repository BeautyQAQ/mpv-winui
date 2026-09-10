using System.Runtime.InteropServices;

namespace MpvShell.Player.LibMpv.Native;

internal static class MpvNodeReader
{
    // 所有事件数据均在下一次 mpv_wait_event 前复制，不释放由 libmpv 拥有的事件内存。
    internal static object? ReadProperty(MpvEventProperty property)
    {
        if (property.Format == MpvFormat.None || property.Data == 0)
            return null;

        return property.Format switch
        {
            MpvFormat.Node => Read(Marshal.PtrToStructure<MpvNode>(property.Data)),
            MpvFormat.String => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(property.Data)),
            MpvFormat.Flag => Marshal.ReadInt32(property.Data) != 0,
            MpvFormat.Int64 => Marshal.ReadInt64(property.Data),
            MpvFormat.Double => Marshal.PtrToStructure<double>(property.Data),
            _ => null,
        };
    }

    internal static object? Read(MpvNode node, int depth = 0)
    {
        if (depth > 32)
            throw new InvalidDataException("mpv 属性结构嵌套过深。");

        return node.Format switch
        {
            MpvFormat.None => null,
            MpvFormat.String => Marshal.PtrToStringUTF8(node.Pointer),
            MpvFormat.Flag => node.Flag != 0,
            MpvFormat.Int64 => node.Integer,
            MpvFormat.Double => node.Number,
            MpvFormat.NodeArray or MpvFormat.NodeMap => ReadList(node, depth),
            _ => null,
        };
    }

    private static object ReadList(MpvNode node, int depth)
    {
        var list = Marshal.PtrToStructure<MpvNodeList>(node.Pointer);
        if (list.Count is < 0 or > 100_000 || (list.Count > 0 && list.Values == 0))
            throw new InvalidDataException("mpv 属性集合大小无效。");

        var values = new object?[list.Count];
        for (var index = 0; index < list.Count; index++)
            values[index] = Read(Marshal.PtrToStructure<MpvNode>(list.Values + index * 16), depth + 1);

        if (node.Format == MpvFormat.NodeArray)
            return values;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < list.Count; index++)
        {
            var key = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys, index * IntPtr.Size));
            if (key is not null)
                result[key] = values[index];
        }
        return result;
    }
}
