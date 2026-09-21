using System.Diagnostics;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 渲染线程内累计的呈现统计。只由拥有图形资源的线程写入；快照通过渲染队列读取，
/// 不引入跨线程锁或每帧分配。用于把"功能通过"与"自然播放吞吐"分开记录。
/// </summary>
public sealed class RenderStatisticsCollector
{
    // 单项指标默认保留的逐帧样本数（约 18 分钟 60 fps）；超出后仍精确累计次数、均值与最大值，只是分位数按已保留样本估计。
    public const int DefaultSampleCapacity = 65_536;

    private readonly FrameTimingAccumulator _render;
    private readonly FrameTimingAccumulator _present;
    private readonly FrameTimingAccumulator _wakeToPresent;
    private long _windowStart = Stopwatch.GetTimestamp();
    private long _presentedFrames;
    private long _skippedFrames;

    public RenderStatisticsCollector(int sampleCapacity = DefaultSampleCapacity)
    {
        _render = new FrameTimingAccumulator(sampleCapacity);
        _present = new FrameTimingAccumulator(sampleCapacity);
        _wakeToPresent = new FrameTimingAccumulator(sampleCapacity);
    }

    public long PresentedFrames => _presentedFrames;
    public long SkippedFrames => _skippedFrames;

    /// <summary>记录一帧已成功呈现；耗时以毫秒计，唤醒延迟未知时传入 null。</summary>
    public void RecordPresented(double renderMilliseconds, double presentMilliseconds, double? wakeToPresentMilliseconds)
    {
        _presentedFrames++;
        _render.Add(renderMilliseconds);
        _present.Add(presentMilliseconds);
        if (wakeToPresentMilliseconds is { } delay) _wakeToPresent.Add(delay);
    }

    public void RecordSkipped() => _skippedFrames++;

    /// <summary>取当前窗口快照；<paramref name="reset"/> 为 true 时同时开始新的统计窗口。</summary>
    public RenderStatisticsSnapshot Snapshot(bool reset)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_windowStart, now).TotalSeconds;
        var snapshot = new RenderStatisticsSnapshot(
            _presentedFrames, _skippedFrames, elapsed,
            elapsed > 0 ? _presentedFrames / elapsed : 0,
            _render.Summarize(), _present.Summarize(), _wakeToPresent.Summarize());
        if (reset) Reset(now);
        return snapshot;
    }

    public void Reset() => Reset(Stopwatch.GetTimestamp());

    private void Reset(long timestamp)
    {
        _windowStart = timestamp;
        _presentedFrames = 0;
        _skippedFrames = 0;
        _render.Clear();
        _present.Clear();
        _wakeToPresent.Clear();
    }
}

/// <summary>一个统计窗口内的呈现结果；<see cref="PresentedFramesPerSecond"/> 是窗口内实际呈现帧数除以墙钟时间。</summary>
public sealed record RenderStatisticsSnapshot(
    long PresentedFrames,
    long SkippedFrames,
    double ElapsedSeconds,
    double PresentedFramesPerSecond,
    FrameTimingSummary Render,
    FrameTimingSummary Present,
    FrameTimingSummary WakeToPresent)
{
    public override string ToString() =>
        $"呈现 {PresentedFrames} 帧 / {ElapsedSeconds:0.00} s = {PresentedFramesPerSecond:0.00} fps；跳过 {SkippedFrames} 帧；" +
        $"Render {Render}；Present {Present}；回调至呈现 {WakeToPresent}";
}

/// <summary>逐帧耗时的汇总；分位数由保留样本计算，<see cref="SampledCount"/> 小于 <see cref="Count"/> 时为估计值。</summary>
public sealed record FrameTimingSummary(
    long Count,
    long SampledCount,
    double AverageMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds)
{
    public static FrameTimingSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public override string ToString() => Count == 0 ? "无样本"
        : $"均值 {AverageMilliseconds:0.00} / 中位 {MedianMilliseconds:0.00} / p95 {P95Milliseconds:0.00} / 最大 {MaximumMilliseconds:0.00} ms（{Count} 帧）";
}

/// <summary>固定容量的逐帧耗时累加器；容量用尽后不再保留样本，但计数、总和与最大值保持精确。</summary>
public sealed class FrameTimingAccumulator
{
    private readonly double[] _samples;
    private int _sampled;
    private long _count;
    private double _sum;
    private double _maximum;

    public FrameTimingAccumulator(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new double[capacity];
    }

    public long Count => _count;

    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "帧耗时必须是非负有限值。");
        _count++;
        _sum += milliseconds;
        if (milliseconds > _maximum) _maximum = milliseconds;
        if (_sampled < _samples.Length) _samples[_sampled++] = milliseconds;
    }

    public void Clear()
    {
        _sampled = 0;
        _count = 0;
        _sum = 0;
        _maximum = 0;
    }

    public FrameTimingSummary Summarize()
    {
        if (_count == 0) return FrameTimingSummary.Empty;
        var sorted = new double[_sampled];
        Array.Copy(_samples, sorted, _sampled);
        Array.Sort(sorted);
        return new FrameTimingSummary(_count, _sampled, _sum / _count,
            Percentile(sorted, 0.50), Percentile(sorted, 0.95), _maximum);
    }

    /// <summary>最近秩法：取不小于 p·n 的最小秩，避免插值制造样本中不存在的耗时。</summary>
    internal static double Percentile(double[] sorted, double fraction)
    {
        if (sorted.Length == 0) return 0;
        var rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }
}
