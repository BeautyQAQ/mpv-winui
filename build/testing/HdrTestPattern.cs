using System;
using System.IO;

// 自行合成的测试图案，不读取或使用任何外部媒体。
public static class HdrTestPattern
{
    public const int Width = 3840;
    public const int Height = 2160;
    public static readonly double[] PatchNits = { 0, 0.005, 0.05, 0.5, 5, 50, 100, 203, 400, 600, 1000 };

    // ST 2084 OETF；输入是绝对亮度 nits，输出为 10-bit limited-range 中性灰 Y'。
    // 常数与仓库锁定 libplacebo src/colorspace.h 的 PQ 常数一致。
    public static ushort NitsToCode(double nits)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        var linear = Math.Pow(nits / 10000.0, m1);
        var pq = Math.Pow((c1 + c2 * linear) / (1 + c3 * linear), m2);
        return (ushort)Math.Round(64 + 876 * pq, MidpointRounding.AwayFromZero);
    }

    public static void WriteRaw(string path)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("测试源要求小端平台。");
        var ramp = new ushort[Width];
        var darkRamp = new ushort[Width];
        var patches = new ushort[Width];
        var chroma = new ushort[Width / 2];
        var maximum = NitsToCode(1000);
        for (var x = 0; x < Width; x++)
        {
            // 整数码值等距渐变，保留 660 个不同的 Y' 值，避免先生成 8-bit 再抬位深。
            ramp[x] = (ushort)(64 + Math.Round((maximum - 64.0) * x / (Width - 1)));
            darkRamp[x] = NitsToCode(5.0 * x / (Width - 1));
            patches[x] = NitsToCode(PatchNits[Math.Min(PatchNits.Length - 1, x * PatchNits.Length / Width)]);
        }
        Array.Fill(chroma, (ushort)512);
        using var output = File.Create(path);
        for (var y = 0; y < Height; y++)
            WriteRow(output, y < 1080 ? ramp : y < 1440 ? darkRamp : patches);
        for (var plane = 0; plane < 2; plane++)
            for (var y = 0; y < Height / 2; y++) WriteRow(output, chroma);
    }

    private static void WriteRow(Stream output, ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }
}
