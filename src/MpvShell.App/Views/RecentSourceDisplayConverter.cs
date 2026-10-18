using Microsoft.UI.Xaml.Data;

namespace MpvShell.App.Views;

public sealed class RecentSourceDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Format(value as string, string.Equals(parameter as string, "Tooltip", StringComparison.Ordinal));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("最近打开记录的显示文本不能转换回媒体地址。");

    internal static string Format(string? source, bool tooltip = false)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            // 显示层隐藏查询、片段及用户凭据；打开时仍使用列表中的原始地址。
            var path = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped);
            return $"{(tooltip ? uri.Scheme + "://" : string.Empty)}{uri.Authority}/{path}";
        }

        if (tooltip) return source;
        var fileName = Path.GetFileName(source);
        return string.IsNullOrEmpty(fileName) ? source : fileName;
    }
}
