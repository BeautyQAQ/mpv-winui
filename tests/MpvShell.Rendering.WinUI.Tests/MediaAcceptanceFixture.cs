using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.LibMpv;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class MediaAcceptanceFactAttribute : FactAttribute
{
    public MediaAcceptanceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MPVSHELL_TEST_P1_04_MEDIA_DIR")))
            Skip = "设置 MPVSHELL_TEST_P1_04_MEDIA_DIR，使用 prepare-p1-04-media.ps1 生成并核验固定视频 HLS/字幕素材。";
    }
}

/// <summary>样本哈希、事件收集与验收报告；不在测试中调用外部编码器或访问公网。</summary>
internal sealed class MediaAcceptanceFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _observeCancellation = new();
    private Task _observer = Task.CompletedTask;
    private readonly string _scenario;
    private readonly Dictionary<string, object?> _evidence = new();
    private bool _passed;
    private readonly CaptureListener _logs = new();
    internal string DirectoryPath { get; }
    internal ConcurrentQueue<PlayerEvent> Events { get; } = new();
    internal ConcurrentQueue<SessionEvent> NativeEvents { get; } = new();
    private MpvPlayerSession? _session;
    internal string LogText => _logs.ToString();

    internal MediaAcceptanceFixture(string scenario)
    {
        _scenario = scenario;
        DirectoryPath = Path.GetFullPath(Environment.GetEnvironmentVariable("MPVSHELL_TEST_P1_04_MEDIA_DIR")!);
        var manifestPath = Path.Combine(DirectoryPath, "media-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        manifest.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(DirectoryPath, file.GetProperty("name").GetString()!));
            path.Should().StartWith(DirectoryPath + Path.DirectorySeparatorChar);
            using var stream = File.OpenRead(path);
            Convert.ToHexString(SHA256.HashData(stream)).Should().Be(file.GetProperty("sha256").GetString(), path);
        }
        _evidence["Scenario"] = scenario;
        _evidence["StartedAtUtc"] = DateTimeOffset.UtcNow;
        _evidence["ManifestSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath)));
        Trace.Listeners.Add(_logs);
    }

    internal string FilePath(string name) => Path.Combine(DirectoryPath, name);
    internal void Record(string key, object? value) => _evidence[key] = value;
    internal void Pass() => _passed = true;

    internal void Observe(LibMpvBackend backend, MpvPlayerSession? session = null)
    {
        _session = session;
        if (session is not null) session.EventReceived += NativeEvents.Enqueue;
        _observer = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in backend.ObserveEventsAsync(_observeCancellation.Token)) Events.Enqueue(item);
            }
            catch (OperationCanceledException) when (_observeCancellation.IsCancellationRequested) { }
        });
    }

    internal static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        while (!await condition()) await Task.Delay(25, cancellationToken);
    }

    internal static Task WaitPositionAsync(MpvPlayerSession session, double position, CancellationToken token) =>
        WaitUntilAsync(async () => await session.GetPropertyAsync("time-pos", token) is double current && current >= position, token);

    internal static async Task SeekPausedAsync(LibMpvBackend backend, MpvPlayerSession session, double target, CancellationToken token)
    {
        await backend.PauseAsync(token);
        await backend.SetPositionAsync(target, token);
        await WaitUntilAsync(async () => await session.GetPropertyAsync("seeking", token) is false &&
            await session.GetPropertyAsync("time-pos", token) is double position && Math.Abs(position - target) < 0.1, token);
    }

    public async ValueTask DisposeAsync()
    {
        _observeCancellation.Cancel();
        try { await _observer; }
        finally
        {
            _observeCancellation.Dispose();
            Trace.Listeners.Remove(_logs);
            _logs.Dispose();
            if (_session is not null) _session.EventReceived -= NativeEvents.Enqueue;
            _evidence["Status"] = _passed ? "Passed" : "Failed";
            _evidence["FinishedAtUtc"] = DateTimeOffset.UtcNow;
            _evidence["EventCounts"] = Events.GroupBy(item => item.GetType().Name).ToDictionary(group => group.Key, group => group.Count());
            _evidence["NativeMediaEvents"] = NativeEvents.Where(item => item.Property is "audio-codec-name" or "track-list" or "eof-reached"
                or "cache-buffering-state" or "video-params" || item.Id is Player.LibMpv.Native.MpvEventId.EndFile)
                .Select(item => new { item.Id, item.Property, item.Value, Error = item.Error?.Message }).ToArray();
            var directory = Environment.GetEnvironmentVariable("MPVSHELL_TEST_P1_04_REPORT_DIR") ?? Path.Combine(DirectoryPath, "..", "reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{_scenario}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"),
                JsonSerializer.Serialize(_evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class CaptureListener : TraceListener
    {
        private readonly StringBuilder _text = new();
        public override void Write(string? message) { lock (_text) _text.Append(message); }
        public override void WriteLine(string? message) { lock (_text) _text.AppendLine(message); }
        public override string ToString() { lock (_text) return _text.ToString(); }
    }
}
