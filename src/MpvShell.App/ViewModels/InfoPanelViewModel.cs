using CommunityToolkit.Mvvm.ComponentModel;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public sealed class InfoPanelViewModel : ObservableObject
{
    private string _videoSummary = "视频信息待加载";
    private string _audioSummary = "音频信息待加载";
    private string _hdrSummary = "SDR / Unknown";

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

    public void Update(InfoPanelSnapshot snapshot)
    {
        VideoSummary = $"{ValueOrFallback(snapshot.VideoCodec)} | {ValueOrFallback(snapshot.Resolution)} | {ValueOrFallback(snapshot.BitDepth)} | {ValueOrFallback(snapshot.FrameRate)}";
        AudioSummary = $"{ValueOrFallback(snapshot.AudioCodec)} | {ValueOrFallback(snapshot.CacheState)}";
        HdrSummary = ValueOrFallback(snapshot.HdrType, "SDR / Unknown");
    }

    private static string ValueOrFallback(string? value, string fallback = "Unknown") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
