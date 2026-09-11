using CommunityToolkit.Mvvm.ComponentModel;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public sealed class InfoPanelViewModel : ObservableObject
{
    private string _videoSummary = "视频信息待加载";
    private string _audioSummary = "音频信息待加载";
    private string _hdrSummary = "SDR / Unknown";
    private string _decodeSummary = "解码信息待加载";
    private string _outputSummary = "显示输出待初始化";
    private bool _isHdrSource;

    public string VideoSummary
    {
        get => _videoSummary;
        private set => SetProperty(ref _videoSummary, value);
    }

    public string AudioSummary
    {
        get => _audioSummary;
        private set => SetProperty(ref _audioSummary, value);
    }

    public string HdrSummary
    {
        get => _hdrSummary;
        private set => SetProperty(ref _hdrSummary, value);
    }

    public string DecodeSummary
    {
        get => _decodeSummary;
        private set => SetProperty(ref _decodeSummary, value);
    }

    public string OutputSummary
    {
        get => _outputSummary;
        private set => SetProperty(ref _outputSummary, value);
    }

    public bool IsHdrSource
    {
        get => _isHdrSource;
        private set => SetProperty(ref _isHdrSource, value);
    }

    public void SetOutputSummary(string summary) => OutputSummary = ValueOrFallback(summary, "显示输出待初始化");

    public void Update(InfoPanelSnapshot snapshot)
    {
        VideoSummary = $"{ValueOrFallback(snapshot.VideoCodec)} | {ValueOrFallback(snapshot.Resolution)} | {ValueOrFallback(snapshot.BitDepth)} | {ValueOrFallback(snapshot.FrameRate)}";
        AudioSummary = $"{ValueOrFallback(snapshot.AudioCodec)} | {ValueOrFallback(snapshot.CacheState)}";
        HdrSummary = ValueOrFallback(snapshot.HdrType, "SDR / Unknown");
        IsHdrSource = snapshot.DynamicRange is VideoDynamicRange.Pq or VideoDynamicRange.Hlg;
        var diagnostics = snapshot.DecodeDiagnostics;
        var decoder = diagnostics?.Mode switch
        {
            VideoDecodeMode.D3D11 when diagnostics.IsGpuResident => "D3D11 硬解 · GPU 纹理传递",
            VideoDecodeMode.D3D11 => "D3D11 硬解 · 纹理互操作待确认",
            VideoDecodeMode.Software => "软件解码（当前格式或设备未启用硬解）",
            VideoDecodeMode.CopyBack => $"{diagnostics.Decoder} · CPU 回拷路径",
            VideoDecodeMode.OtherHardware => $"硬件解码：{diagnostics.Decoder}",
            _ => "解码信息待加载",
        };
        DecodeSummary = $"{decoder} | 解码丢帧 {FormatCounter(diagnostics?.DecoderDroppedFrames)} | 呈现丢帧 {FormatCounter(diagnostics?.PresentationDroppedFrames)}";
    }

    private static string FormatCounter(long? count) => count?.ToString() ?? "未知";

    private static string ValueOrFallback(string? value, string fallback = "Unknown") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
