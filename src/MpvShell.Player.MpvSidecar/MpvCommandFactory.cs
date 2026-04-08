using System.Text.Json;

namespace MpvShell.Player.MpvSidecar;

public static class MpvCommandFactory
{
    public static string LoadUrl(string url) =>
        JsonSerializer.Serialize(new { command = new object[] { "loadfile", url, "replace" } });

    public static string Observe(string propertyName, int id) =>
        JsonSerializer.Serialize(new { command = new object[] { "observe_property", id, propertyName } });

    public static string SeekRelative(double seconds) =>
        JsonSerializer.Serialize(new { command = new object[] { "seek", seconds, "relative" } });

    public static string SeekAbsolute(double seconds) =>
        JsonSerializer.Serialize(new { command = new object[] { "seek", seconds, "absolute" } });

    public static string SetProperty(string name, object value) =>
        JsonSerializer.Serialize(new { command = new object[] { "set_property", name, value } });
}
