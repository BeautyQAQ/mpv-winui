namespace MpvShell.App.Diagnostics;

internal static class PlaybackProbeSampling
{
    internal static double GetInterval(double durationSeconds, double positionSeconds)
    {
        // 片尾前一秒不采样；剩余区间分四段，使短片也能完成至少两次动态图像检查。
        var available = durationSeconds - 1 - positionSeconds;
        if (!double.IsFinite(available) || available <= 0)
            throw new InvalidOperationException("剩余播放时间不足以验证动态图像。");
        return Math.Min(5, available / 4);
    }
}
