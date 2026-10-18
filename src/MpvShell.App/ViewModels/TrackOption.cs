using CommunityToolkit.Mvvm.ComponentModel;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

/// <summary>面板展示项；选中标记仅来自后端的轨道列表。</summary>
public sealed class TrackOption(TrackInfo track) : ObservableObject
{
    private TrackInfo _track = track;
    public TrackInfo Track => _track;
    public bool IsSelected => Track.Selected;
    public string SelectionGlyph => IsSelected ? "\uE73E" : string.Empty;
    public string Label => Track.Id <= 0 && Track.Kind.Equals("sub", StringComparison.OrdinalIgnoreCase)
        ? "关闭字幕"
        : string.IsNullOrWhiteSpace(Track.Title)
            ? $"{(Track.Kind.Equals("audio", StringComparison.OrdinalIgnoreCase) ? "音轨" : "字幕")} {Track.Id}"
            : Track.Title.Trim();
    public string Detail => Track.Id <= 0 ? "不显示字幕" : $"{LanguageLabel(Track.Language)} · 轨道 {Track.Id}";
    public string AccessibleLabel => $"{Label}，{Detail}{(IsSelected ? "，已选择" : string.Empty)}";

    internal void Update(TrackInfo track)
    {
        if (!SetProperty(ref _track, track, nameof(Track))) return;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectionGlyph));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(AccessibleLabel));
    }

    private static string LanguageLabel(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        null or "" or "und" => "语言未标注",
        "zh" or "zho" or "chi" => "中文",
        "zh-cn" or "zh-hans" => "简体中文",
        "zh-tw" or "zh-hant" => "繁体中文",
        "en" or "eng" => "英语",
        "ja" or "jpn" => "日语",
        "ko" or "kor" => "韩语",
        "fr" or "fra" or "fre" => "法语",
        "de" or "deu" or "ger" => "德语",
        "es" or "spa" => "西班牙语",
        "ru" or "rus" => "俄语",
        _ => language.Trim(),
    };
}
