namespace MpvShell.App.Services;

public enum PlayerGesture
{
    None,
    Seek,
    Volume,
}

public sealed class GestureDecisionEngine
{
    public PlayerGesture Classify(double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) > Math.Abs(deltaY) && Math.Abs(deltaX) > 40)
        {
            return PlayerGesture.Seek;
        }

        if (Math.Abs(deltaY) > 40)
        {
            return PlayerGesture.Volume;
        }

        return PlayerGesture.None;
    }
}
