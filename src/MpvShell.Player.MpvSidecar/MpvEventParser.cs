using System.Text.Json;

namespace MpvShell.Player.MpvSidecar;

public sealed record ParsedMpvEvent(
    string EventName,
    string? PropertyName,
    bool? BooleanValue,
    JsonElement? RawData);

public static class MpvEventParser
{
    public static ParsedMpvEvent Parse(string jsonLine)
    {
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;

        var eventName = root.TryGetProperty("event", out var evt) ? evt.GetString() ?? "unknown" : "unknown";
        var propertyName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        bool? boolValue = null;
        JsonElement? rawData = null;

        if (root.TryGetProperty("data", out var data))
        {
            rawData = data.Clone();

            if (data.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                boolValue = data.GetBoolean();
            }
        }

        return new ParsedMpvEvent(eventName, propertyName, boolValue, rawData);
    }
}
