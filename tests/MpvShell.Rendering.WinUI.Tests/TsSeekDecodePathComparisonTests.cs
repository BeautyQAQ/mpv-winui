using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MpvShell.Player.LibMpv;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>
/// RTX 3070 日志中的 LG TS 样片每次跳转后都出现 HEVC “Could not find ref with POC” 错误组。
/// 根因：libavformat 的 MPEG-TS 没有关键帧索引，底层 seek 落在 GOP 中间，而 mpv 对新鲜的 demuxer seek
/// 不会等到关键帧再把包交给解码器。锁定 libmpv 的 mpv-demux-seek-skip-to-keyframe 补丁提供
/// demuxer-skip-to-keyframe 选项修正此问题。本测试用同一文件、同一组目标时间对照：产品配置（硬解 / 软解，
/// 关键帧起读）必须 0 错误且精确落点；关闭该选项的变体保留为改动前的对照；顺播阶段任何变体都不应有错误。
/// 设置 MPVSHELL_TEST_TS_MEDIA 指向 TS 文件后运行（合成样片见 build/testing/prepare-ts-seek-media.ps1）；
/// 目标时间可用 MPVSHELL_TEST_TS_SEEK_TARGETS 覆盖，顺播秒数可用 MPVSHELL_TEST_TS_SEQUENTIAL_SECONDS 覆盖。
/// </summary>
[Collection("HardwareMedia")]
public sealed class TsSeekDecodePathComparisonTests
{
    [HardwareMediaFact("MPVSHELL_TEST_TS_MEDIA")]
    [Trait("Category", "HardwareMedia")]
    public async Task Seek_reference_errors_should_be_compared_across_sequential_hardware_and_software_decoding()
    {
        var path = Environment.GetEnvironmentVariable("MPVSHELL_TEST_TS_MEDIA")!;
        File.Exists(path).Should().BeTrue("MPVSHELL_TEST_TS_MEDIA 必须指向存在的 MPEG-TS 样片");
        var targets = (Environment.GetEnvironmentVariable("MPVSHELL_TEST_TS_SEEK_TARGETS")
                ?? "24,44,61,77,95,111,86,60,40,89,120,64,35,22,82,100,115")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => double.Parse(text, CultureInfo.InvariantCulture)).ToArray();
        var sequentialSeconds = double.Parse(Environment.GetEnvironmentVariable("MPVSHELL_TEST_TS_SEQUENTIAL_SECONDS") ?? "20", CultureInfo.InvariantCulture);
        var reportDirectory = Environment.GetEnvironmentVariable("MPVSHELL_TEST_HARDWARE_REPORT_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "hardware-reports");
        Directory.CreateDirectory(reportDirectory);

        var report = new ComparisonReport { MediaFile = Path.GetFileName(path), SeekTargets = targets, SequentialSeconds = sequentialSeconds };
        // 前两项是产品配置（硬解 / 软解），第三项关闭关键帧起读作为改动前的对照，第四项关闭 demuxer 回退窗口。
        var variants = new (string Decoder, string DemuxerOffset, string SkipToKeyframe)[]
        {
            ("d3d11va", "1", "yes"), ("no", "1", "yes"), ("d3d11va", "1", "no"), ("d3d11va", "0", "yes"),
        };
        try
        {
            foreach (var (decoder, offset, skip) in variants)
                report.Variants.Add(await RunVariantAsync(path, decoder, offset, skip, targets, sequentialSeconds));
        }
        finally
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "ts-seek-decode-path-comparison.json"), JsonSerializer.Serialize(report, options));
        }

        var hardware = report.Variants[0];
        var software = report.Variants[1];
        var legacy = report.Variants[2];
        hardware.ActualDecoder.Should().Be("d3d11va", "对照的前提是硬解实际生效");
        software.ActualDecoder.Should().Be("no");
        foreach (var variant in report.Variants)
            variant.SequentialErrorGroups.Should().Be(0, $"{variant.Decoder} 顺播阶段不应出现参考帧错误");
        (software.TotalSeekErrorGroups > 0).Should().Be(hardware.TotalSeekErrorGroups > 0,
            "若错误只在某一种解码路径出现，才能归因为解码路径问题");
        // 产品配置：视频流从关键帧起读，跳转后不应再有任何参考帧错误，且精确 seek 仍落在目标附近。
        hardware.TotalSeekErrorGroups.Should().Be(0, "关键帧起读后解码器不应收到无法参考的前导帧");
        software.TotalSeekErrorGroups.Should().Be(0);
        foreach (var seek in hardware.Seeks)
            seek.RestartPositionSeconds.Should().BeApproximately(seek.Target, 0.1,
                "demuxer 回退窗口应让目标之前存在关键帧，hr-seek 精确到达目标");
        if (legacy.TotalSeekErrorGroups == 0)
            Trace.WriteLine("[TS seek] 对照变体也没有错误：该样片的 demuxer 落点本身就在关键帧上，无法证明补丁生效。");
    }

    private static async Task<VariantResult> RunVariantAsync(string path, string decoder, string demuxerOffset,
        string skipToKeyframe, double[] targets, double sequentialSeconds)
    {
        var result = new VariantResult { Decoder = decoder, HrSeekDemuxerOffset = demuxerOffset, SkipToKeyframe = skipToKeyframe };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var counter = new HevcLogCounter();
        Trace.Listeners.Add(counter);
        try
        {
            await using var session = new MpvPlayerSession(new Dictionary<string, string>
            {
                ["ao"] = "null", ["hwdec"] = decoder, ["hr-seek-demuxer-offset"] = demuxerOffset,
                ["demuxer-skip-to-keyframe"] = skipToKeyframe,
            }, "v");
            await using var backend = new LibMpvBackend(session);
            await backend.InitializeAsync(cancellation.Token);
            // 与应用相同：先创建 d3d11-egl 互操作的渲染上下文，硬解才会实际生效；不绑定表面也能解码。
            await using var renderer = new D3D11VideoSurfaceRenderer();
            await renderer.InitializeAsync(session, cancellation.Token);

            counter.Reset();
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, sequentialSeconds, cancellation.Token);
            result.SequentialErrorGroups = counter.MissingReferenceGroups;
            result.ActualDecoder = await session.GetPropertyAsync("hwdec-current", cancellation.Token) as string ?? "";

            foreach (var target in targets)
            {
                var restartsBefore = counter.PlaybackRestarts;
                var groupsBefore = counter.MissingReferenceGroups;
                var trailBefore = counter.SkippedTrailingNalus;
                var raslBefore = counter.SkippedRaslNalus;
                var clock = Stopwatch.StartNew();
                await backend.SetPositionAsync(target, cancellation.Token);
                while (counter.PlaybackRestarts == restartsBefore)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await Task.Delay(10, cancellation.Token);
                }
                var latency = clock.Elapsed.TotalMilliseconds;
                // 错误在重启后的首批解码里就会出现；再留一小段时间排空日志。
                await Task.Delay(400, cancellation.Token);
                result.Seeks.Add(new SeekResult
                {
                    Target = target,
                    RestartPositionSeconds = counter.LastRestartPosition,
                    RestartLatencyMilliseconds = Math.Round(latency, 1),
                    MissingReferenceGroups = counter.MissingReferenceGroups - groupsBefore,
                    SkippedTrailingNalus = counter.SkippedTrailingNalus - trailBefore,
                    SkippedRaslNalus = counter.SkippedRaslNalus - raslBefore,
                });
            }
            result.FrameDropCount = await session.GetPropertyAsync("frame-drop-count", cancellation.Token) as long? ?? -1;

            await session.CommandAsync(["stop"], cancellation.Token);
            while (await session.GetPropertyAsync("idle-active", cancellation.Token) is not true)
                await Task.Delay(20, cancellation.Token);
        }
        finally
        {
            Trace.Listeners.Remove(counter);
        }
        return result;
    }

    private static async Task WaitForPositionAsync(MpvPlayerSession session, double seconds, CancellationToken cancellationToken)
    {
        while (await session.GetPropertyAsync("time-pos", cancellationToken) is not double position || position < seconds)
            await Task.Delay(50, cancellationToken);
    }

    private sealed class HevcLogCounter : TraceListener
    {
        private int _groups, _trail, _rasl, _restarts;
        private double _lastRestart;

        public int MissingReferenceGroups => Volatile.Read(ref _groups);
        public int SkippedTrailingNalus => Volatile.Read(ref _trail);
        public int SkippedRaslNalus => Volatile.Read(ref _rasl);
        public int PlaybackRestarts => Volatile.Read(ref _restarts);
        public double LastRestartPosition => Volatile.Read(ref _lastRestart);

        public void Reset()
        {
            Interlocked.Exchange(ref _groups, 0);
            Interlocked.Exchange(ref _trail, 0);
            Interlocked.Exchange(ref _rasl, 0);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (message is null) return;
            if (message.Contains("Could not find ref with POC", StringComparison.Ordinal)) Interlocked.Increment(ref _groups);
            else if (message.Contains("Skipping invalid undecodable NALU: 1", StringComparison.Ordinal)) Interlocked.Increment(ref _trail);
            else if (message.Contains("Skipping invalid undecodable NALU: 9", StringComparison.Ordinal)) Interlocked.Increment(ref _rasl);
            else if (message.Contains("playback restart complete @ ", StringComparison.Ordinal))
            {
                var start = message.IndexOf("complete @ ", StringComparison.Ordinal) + "complete @ ".Length;
                var end = message.IndexOf(',', start);
                if (end > start && double.TryParse(message.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var position))
                    Volatile.Write(ref _lastRestart, position);
                Interlocked.Increment(ref _restarts);
            }
        }
    }

    private sealed class ComparisonReport
    {
        public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;
        public required string MediaFile { get; init; }
        public required double[] SeekTargets { get; init; }
        public double SequentialSeconds { get; init; }
        public List<VariantResult> Variants { get; } = [];
    }

    private sealed class VariantResult
    {
        public required string Decoder { get; init; }
        public required string HrSeekDemuxerOffset { get; init; }
        public required string SkipToKeyframe { get; init; }
        public string ActualDecoder { get; set; } = "";
        public int SequentialErrorGroups { get; set; }
        public long FrameDropCount { get; set; }
        public int TotalSeekErrorGroups => Seeks.Sum(seek => seek.MissingReferenceGroups);
        public int SeeksWithErrors => Seeks.Count(seek => seek.MissingReferenceGroups > 0);
        public List<SeekResult> Seeks { get; } = [];
    }

    private sealed class SeekResult
    {
        public double Target { get; init; }
        public double RestartPositionSeconds { get; init; }
        public double RestartLatencyMilliseconds { get; init; }
        public int MissingReferenceGroups { get; init; }
        public int SkippedTrailingNalus { get; init; }
        public int SkippedRaslNalus { get; init; }
    }
}
