namespace MpvShell.Player.Abstractions.Models;

public sealed record TrackInfo(int Id, string Kind, string? Language, string? Title, bool Selected);
