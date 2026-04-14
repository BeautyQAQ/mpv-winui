# WinUI 3 + mpv 播放器壳层 V1 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal / 目标：** 交付一个可日常使用的 Windows-only WinUI 3 播放器壳层 V1，使用 `mpv.exe + JSON IPC` 作为首版后端，支持 URL 播放、底部大控件、时间轴拖拽、字幕/音轨切换、OSD 菜单、详细 HDR 信息面板和自动隐藏控件。

**Architecture / 架构：** 采用 4 层结构：WinUI 3 表现层、交互协调层、播放器后端抽象层、`MpvSidecarBackend` 后端实现层。V1 聚焦 `Phase 0` 技术可行性和 `Phase 1` 可用产品；`Phase 2/3` 不在本计划执行范围内。

**Tech Stack / 技术栈：** .NET 8、WinUI 3、Windows App SDK、C#、xUnit、FluentAssertions、`mpv.exe` JSON IPC、Win32 窗口互操作。

---

## 文件结构

### 根目录

- Create: `MpvShell.slnx`
- Create: `Directory.Build.props`

### 应用层

- Create: `src/MpvShell.App/MpvShell.App.csproj`
- Create: `src/MpvShell.App/App.xaml`
- Create: `src/MpvShell.App/App.xaml.cs`
- Create: `src/MpvShell.App/MainWindow.xaml`
- Create: `src/MpvShell.App/MainWindow.xaml.cs`
- Create: `src/MpvShell.App/Views/PlayerPage.xaml`
- Create: `src/MpvShell.App/Views/PlayerPage.xaml.cs`
- Create: `src/MpvShell.App/ViewModels/PlayerViewModel.cs`
- Create: `src/MpvShell.App/ViewModels/InfoPanelViewModel.cs`
- Create: `src/MpvShell.App/Services/PlaybackInteractionCoordinator.cs`
- Create: `src/MpvShell.App/Services/GestureDecisionEngine.cs`
- Create: `src/MpvShell.App/Services/RecentUrlStore.cs`
- Create: `src/MpvShell.App/Styles/PlayerStyles.xaml`

### 抽象层

- Create: `src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj`
- Create: `src/MpvShell.Player.Abstractions/IPlayerBackend.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/PlaybackState.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/TrackInfo.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/InfoPanelSnapshot.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/OverlayKind.cs`
- Create: `src/MpvShell.Player.Abstractions/Events/PlayerEvent.cs`

### mpv Sidecar 后端

- Create: `src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj`
- Create: `src/MpvShell.Player.MpvSidecar/MpvLaunchOptions.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvProcessManager.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvCommandFactory.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvJsonIpcClient.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvEventParser.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvSidecarBackend.cs`

### 视频宿主与互操作

- Create: `src/MpvShell.Interop.VideoHost/MpvShell.Interop.VideoHost.csproj`
- Create: `src/MpvShell.Interop.VideoHost/VideoHostControl.cs`
- Create: `src/MpvShell.Interop.VideoHost/HostBoundsTranslator.cs`
- Create: `src/MpvShell.Interop.VideoHost/NativeMethods.cs`

### 测试

- Create: `tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj`
- Create: `tests/MpvShell.Player.Abstractions.Tests/PlaybackStateTests.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvCommandFactoryTests.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvEventParserTests.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvProcessManagerTests.cs`
- Create: `tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj`
- Create: `tests/MpvShell.App.Tests/PlaybackInteractionCoordinatorTests.cs`
- Create: `tests/MpvShell.App.Tests/GestureDecisionEngineTests.cs`
- Create: `tests/MpvShell.App.Tests/RecentUrlStoreTests.cs`
- Create: `tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj`
- Create: `tests/MpvShell.Interop.VideoHost.Tests/HostBoundsTranslatorTests.cs`

## 范围锁定

本计划只覆盖以下内容：

- `Phase 0`：视频宿主原型、mpv 播放通路、覆盖层和输入事件可行性
- `Phase 1`：URL 播放、底部大控件、时间轴拖拽、OSD、字幕/音轨切换、详细信息面板、自动隐藏控件

本计划明确不覆盖以下内容：

- 媒体库、刮削、海报墙
- DRM 和认证流
- 亮度手势
- 完整播放列表增强和多后端切换

### Task 1: 搭建解决方案骨架与统一状态模型

**Files:**
- Create: `MpvShell.slnx`
- Create: `Directory.Build.props`
- Create: `src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj`
- Create: `src/MpvShell.Player.Abstractions/IPlayerBackend.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/PlaybackState.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/TrackInfo.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/InfoPanelSnapshot.cs`
- Create: `src/MpvShell.Player.Abstractions/Models/OverlayKind.cs`
- Create: `src/MpvShell.Player.Abstractions/Events/PlayerEvent.cs`
- Create: `tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj`
- Create: `tests/MpvShell.Player.Abstractions.Tests/PlaybackStateTests.cs`

- [ ] **Step 1: 写失败测试，锁定 V1 状态模型**

```csharp
using FluentAssertions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions.Tests;

public sealed class PlaybackStateTests
{
    [Fact]
    public void Initial_state_should_match_v1_defaults()
    {
        var state = PlaybackState.Initial;

        state.IsPlaying.Should().BeFalse();
        state.PositionSeconds.Should().Be(0);
        state.DurationSeconds.Should().Be(0);
        state.Volume.Should().Be(100);
        state.CurrentOverlay.Should().Be(OverlayKind.None);
        state.AreControlsVisible.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 创建解决方案、项目和引用，运行测试确认失败**

Run:

```bash
dotnet new sln -n MpvShell -f slnx
dotnet new classlib -n MpvShell.Player.Abstractions -o src/MpvShell.Player.Abstractions
dotnet new xunit -n MpvShell.Player.Abstractions.Tests -o tests/MpvShell.Player.Abstractions.Tests
dotnet sln MpvShell.slnx add src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj
dotnet sln MpvShell.slnx add tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj
dotnet add tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj reference src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj
dotnet add tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj package FluentAssertions
dotnet test tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj -v minimal
```

Expected: FAIL，报 `PlaybackState`、`OverlayKind` 或 `Initial` 未定义。

- [ ] **Step 3: 写最小实现，固定抽象层骨架**

```csharp
namespace MpvShell.Player.Abstractions.Models;

public enum OverlayKind
{
    None,
    Osd,
    Tracks,
    InfoPanel,
}

public sealed record TrackInfo(int Id, string Kind, string? Language, string? Title, bool Selected);

public sealed record InfoPanelSnapshot(
    string? VideoCodec,
    string? AudioCodec,
    string? HdrType,
    string? Resolution,
    string? BitDepth,
    string? FrameRate,
    string? CacheState);

public sealed record PlaybackState(
    string? CurrentUrl,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    int Volume,
    bool IsMuted,
    bool AreControlsVisible,
    OverlayKind CurrentOverlay)
{
    public static PlaybackState Initial =>
        new(null, false, 0, 0, 100, false, false, OverlayKind.None);
}
```

```csharp
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions;

public interface IPlayerBackend : IAsyncDisposable
{
    Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken);
    Task LoadUrlAsync(string url, CancellationToken cancellationToken);
    Task PlayAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken);
    Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken);
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken);
    Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken);
    Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken);
    Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlayerEvent> ObserveEventsAsync(CancellationToken cancellationToken);
}
```

```csharp
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.Abstractions.Events;

public abstract record PlayerEvent;

public sealed record PlaybackStateChanged(PlaybackState State) : PlayerEvent;

public sealed record TracksChanged(IReadOnlyList<TrackInfo> Tracks) : PlayerEvent;

public sealed record BackendFaulted(string Message) : PlayerEvent;
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```bash
dotnet test tests/MpvShell.Player.Abstractions.Tests/MpvShell.Player.Abstractions.Tests.csproj -v minimal
```

Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add MpvShell.slnx Directory.Build.props src/MpvShell.Player.Abstractions tests/MpvShell.Player.Abstractions.Tests
git commit -m "feat: scaffold player abstractions"
```

### Task 2: 实现 mpv JSON IPC 命令与事件解析

**Files:**
- Create: `src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj`
- Create: `src/MpvShell.Player.MpvSidecar/MpvCommandFactory.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvEventParser.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvJsonIpcClient.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvCommandFactoryTests.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvEventParserTests.cs`

- [ ] **Step 1: 先写失败测试，锁定命令格式与事件映射**

```csharp
using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvCommandFactoryTests
{
    [Fact]
    public void Seek_command_should_match_json_ipc_shape()
    {
        var json = MpvCommandFactory.SeekRelative(15);

        json.Should().Contain("\"command\"");
        json.Should().Contain("\"seek\"");
        json.Should().Contain("15");
        json.Should().Contain("\"relative\"");
    }
}
```

```csharp
using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvEventParserTests
{
    [Fact]
    public void Property_change_event_should_extract_pause_state()
    {
        const string line = """
        {"event":"property-change","name":"pause","data":false}
        """;

        var parsed = MpvEventParser.Parse(line);

        parsed.EventName.Should().Be("property-change");
        parsed.PropertyName.Should().Be("pause");
        parsed.BooleanValue.Should().BeFalse();
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet new classlib -n MpvShell.Player.MpvSidecar -o src/MpvShell.Player.MpvSidecar
dotnet new xunit -n MpvShell.Player.MpvSidecar.Tests -o tests/MpvShell.Player.MpvSidecar.Tests
dotnet sln MpvShell.slnx add src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj
dotnet sln MpvShell.slnx add tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj
dotnet add src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj reference src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj
dotnet add tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj reference src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj
dotnet add tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj package FluentAssertions
dotnet test tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj -v minimal
```

Expected: FAIL，报 `MpvCommandFactory` 或 `MpvEventParser` 未定义。

- [ ] **Step 3: 写最小实现，建立 Sidecar 协议边界**

```csharp
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
```

```csharp
using System.Text.Json;

namespace MpvShell.Player.MpvSidecar;

public sealed record ParsedMpvEvent(string EventName, string? PropertyName, bool? BooleanValue, JsonElement? RawData);

public static class MpvEventParser
{
    public static ParsedMpvEvent Parse(string jsonLine)
    {
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;

        var eventName = root.TryGetProperty("event", out var evt) ? evt.GetString() ?? "unknown" : "unknown";
        var propertyName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        bool? boolValue = null;

        if (root.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            boolValue = data.GetBoolean();
        }

        return new ParsedMpvEvent(eventName, propertyName, boolValue, root.TryGetProperty("data", out var raw) ? raw : null);
    }
}
```

```csharp
using System.IO.Pipes;
using System.Text;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvJsonIpcClient : IAsyncDisposable
{
    private NamedPipeClientStream? _pipe;

    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(cancellationToken);
    }

    public async Task SendAsync(string commandJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_pipe);
        var payload = Encoding.UTF8.GetBytes(commandJson + "\n");
        await _pipe.WriteAsync(payload, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```bash
dotnet test tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj -v minimal
```

Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.Player.MpvSidecar tests/MpvShell.Player.MpvSidecar.Tests
git commit -m "feat: add mpv ipc protocol layer"
```

### Task 3: 实现 mpv 进程管理与 V1 Sidecar 后端

**Files:**
- Create: `src/MpvShell.Player.MpvSidecar/MpvLaunchOptions.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvProcessManager.cs`
- Create: `src/MpvShell.Player.MpvSidecar/MpvSidecarBackend.cs`
- Create: `tests/MpvShell.Player.MpvSidecar.Tests/MpvProcessManagerTests.cs`

- [ ] **Step 1: 写失败测试，锁定进程参数与初始化行为**

```csharp
using FluentAssertions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.Player.MpvSidecar.Tests;

public sealed class MpvProcessManagerTests
{
    [Fact]
    public void Launch_arguments_should_enable_idle_force_window_and_ipc()
    {
        var options = new MpvLaunchOptions("mpv.exe", "mpvshell-test", (nint)1234);
        var args = MpvProcessManager.BuildArguments(options);

        args.Should().Contain("--idle=yes");
        args.Should().Contain("--force-window=yes");
        args.Should().Contain("--input-ipc-server=\\\\.\\pipe\\mpvshell-test");
        args.Should().Contain("--wid=1234");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj -v minimal --filter MpvProcessManagerTests
```

Expected: FAIL，报 `MpvLaunchOptions` 或 `BuildArguments` 未定义。

- [ ] **Step 3: 写最小实现，打通后端初始化通路**

```csharp
namespace MpvShell.Player.MpvSidecar;

public sealed record MpvLaunchOptions(string ExecutablePath, string PipeName, nint HostHandle);
```

```csharp
using System.Diagnostics;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvProcessManager
{
    public static string BuildArguments(MpvLaunchOptions options) =>
        $"--idle=yes --force-window=yes --input-ipc-server=\\\\.\\pipe\\{options.PipeName} --wid={options.HostHandle}";

    public Process Start(MpvLaunchOptions options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            Arguments = BuildArguments(options),
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("mpv.exe 启动失败");
    }
}
```

```csharp
using System.Runtime.CompilerServices;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Events;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.Player.MpvSidecar;

public sealed class MpvSidecarBackend : IPlayerBackend
{
    private readonly MpvProcessManager _processManager = new();
    private readonly MpvJsonIpcClient _ipcClient = new();
    private PlaybackState _state = PlaybackState.Initial;

    public async Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken)
    {
        var pipeName = $"mpvshell-{Environment.ProcessId}";
        var options = new MpvLaunchOptions("mpv.exe", pipeName, hostHandle);
        _processManager.Start(options);
        await _ipcClient.ConnectAsync(pipeName, cancellationToken);
        await _ipcClient.SendAsync(MpvCommandFactory.Observe("pause", 1), cancellationToken);
        await _ipcClient.SendAsync(MpvCommandFactory.Observe("time-pos", 2), cancellationToken);
    }

    public Task LoadUrlAsync(string url, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.LoadUrl(url), cancellationToken);

    public Task PlayAsync(CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("pause", false), cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("pause", true), cancellationToken);

    public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SeekRelative(deltaSeconds), cancellationToken);

    public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SeekAbsolute(absoluteSeconds), cancellationToken);

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("volume", volume), cancellationToken);

    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("mute", muted), cancellationToken);

    public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());

    public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("aid", trackId), cancellationToken);

    public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) =>
        _ipcClient.SendAsync(MpvCommandFactory.SetProperty("sid", trackId), cancellationToken);

    public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));

    public async IAsyncEnumerable<PlayerEvent> ObserveEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield return new PlaybackStateChanged(_state);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 4: 运行测试并执行一次后端烟雾验证**

Run:

```bash
dotnet test tests/MpvShell.Player.MpvSidecar.Tests/MpvShell.Player.MpvSidecar.Tests.csproj -v minimal
```

Expected: PASS

Run:

```bash
dotnet build MpvShell.slnx -v minimal
```

Expected: BUILD SUCCEEDED

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.Player.MpvSidecar tests/MpvShell.Player.MpvSidecar.Tests
git commit -m "feat: implement mpv sidecar backend"
```

### Task 4: 验证视频宿主互操作原型

**Files:**
- Create: `src/MpvShell.Interop.VideoHost/MpvShell.Interop.VideoHost.csproj`
- Create: `src/MpvShell.Interop.VideoHost/HostBoundsTranslator.cs`
- Create: `src/MpvShell.Interop.VideoHost/VideoHostControl.cs`
- Create: `src/MpvShell.Interop.VideoHost/NativeMethods.cs`
- Create: `tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj`
- Create: `tests/MpvShell.Interop.VideoHost.Tests/HostBoundsTranslatorTests.cs`

- [ ] **Step 1: 写失败测试，先固定宿主区域尺寸换算逻辑**

```csharp
using FluentAssertions;
using MpvShell.Interop.VideoHost;

namespace MpvShell.Interop.VideoHost.Tests;

public sealed class HostBoundsTranslatorTests
{
    [Fact]
    public void Should_translate_logical_size_to_pixel_bounds()
    {
        var rect = HostBoundsTranslator.Translate(0, 0, 800, 450, 1.5);

        rect.Width.Should().Be(1200);
        rect.Height.Should().Be(675);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet new classlib -n MpvShell.Interop.VideoHost -o src/MpvShell.Interop.VideoHost
dotnet new xunit -n MpvShell.Interop.VideoHost.Tests -o tests/MpvShell.Interop.VideoHost.Tests
dotnet sln MpvShell.slnx add src/MpvShell.Interop.VideoHost/MpvShell.Interop.VideoHost.csproj
dotnet sln MpvShell.slnx add tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj
dotnet add tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj reference src/MpvShell.Interop.VideoHost/MpvShell.Interop.VideoHost.csproj
dotnet add tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj package FluentAssertions
dotnet test tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj -v minimal
```

Expected: FAIL，报 `HostBoundsTranslator` 未定义。

- [ ] **Step 3: 写最小实现，并创建 Win32 宿主边界**

```csharp
namespace MpvShell.Interop.VideoHost;

public readonly record struct HostRect(int X, int Y, int Width, int Height);

public static class HostBoundsTranslator
{
    public static HostRect Translate(double x, double y, double width, double height, double rasterizationScale) =>
        new(
            (int)Math.Round(x * rasterizationScale),
            (int)Math.Round(y * rasterizationScale),
            (int)Math.Round(width * rasterizationScale),
            (int)Math.Round(height * rasterizationScale));
}
```

```csharp
using Microsoft.UI.Xaml.Controls;

namespace MpvShell.Interop.VideoHost;

public sealed class VideoHostControl : Grid
{
    public nint ChildWindowHandle { get; private set; }

    public void Attach(nint childHandle)
    {
        ChildWindowHandle = childHandle;
    }
}
```

```csharp
using System.Runtime.InteropServices;

namespace MpvShell.Interop.VideoHost;

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);
}
```

- [ ] **Step 4: 跑测试并手工验证原型**

Run:

```bash
dotnet test tests/MpvShell.Interop.VideoHost.Tests/MpvShell.Interop.VideoHost.Tests.csproj -v minimal
```

Expected: PASS

Run:

```bash
dotnet build MpvShell.slnx -v minimal
```

Expected: BUILD SUCCEEDED

Manual check:

```bash
dotnet run --project src/MpvShell.App/MpvShell.App.csproj
```

Expected: 后续在 Task 6 完成前端壳后，能看到独立的视频宿主占位区域；此处先记录为 Phase 0 原型检查点。

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.Interop.VideoHost tests/MpvShell.Interop.VideoHost.Tests
git commit -m "feat: add video host interop prototype"
```

### Task 5: 实现交互协调层与主播放 ViewModel

**Files:**
- Create: `src/MpvShell.App/MpvShell.App.csproj`
- Create: `src/MpvShell.App/ViewModels/PlayerViewModel.cs`
- Create: `src/MpvShell.App/Services/PlaybackInteractionCoordinator.cs`
- Create: `tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj`
- Create: `tests/MpvShell.App.Tests/PlaybackInteractionCoordinatorTests.cs`

- [x] **Step 1: 写失败测试，锁定自动隐藏和浮层切换行为**

```csharp
using FluentAssertions;
using MpvShell.Player.Abstractions.Models;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class PlaybackInteractionCoordinatorTests
{
    [Fact]
    public void Show_controls_should_close_transient_overlay_and_make_controls_visible()
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = PlaybackState.Initial with { CurrentOverlay = OverlayKind.InfoPanel };

        var next = coordinator.ShowControls(state);

        next.AreControlsVisible.Should().BeTrue();
        next.CurrentOverlay.Should().Be(OverlayKind.None);
    }
}
```

- [x] **Step 2: 创建 App 项目和测试项目，运行测试确认失败**

Run:

```bash
dotnet new winui3 -n MpvShell.App -o src/MpvShell.App
dotnet new xunit -n MpvShell.App.Tests -o tests/MpvShell.App.Tests
dotnet sln MpvShell.slnx add src/MpvShell.App/MpvShell.App.csproj
dotnet sln MpvShell.slnx add tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj
dotnet add src/MpvShell.App/MpvShell.App.csproj reference src/MpvShell.Player.Abstractions/MpvShell.Player.Abstractions.csproj
dotnet add src/MpvShell.App/MpvShell.App.csproj reference src/MpvShell.Player.MpvSidecar/MpvShell.Player.MpvSidecar.csproj
dotnet add src/MpvShell.App/MpvShell.App.csproj reference src/MpvShell.Interop.VideoHost/MpvShell.Interop.VideoHost.csproj
dotnet add tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj reference src/MpvShell.App/MpvShell.App.csproj
dotnet add tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj package FluentAssertions
dotnet add src/MpvShell.App/MpvShell.App.csproj package CommunityToolkit.Mvvm
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter PlaybackInteractionCoordinatorTests
```

Expected: FAIL，报 `PlaybackInteractionCoordinator` 未定义。

- [x] **Step 3: 写最小实现，固定 V1 的交互中心**

```csharp
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Services;

public sealed class PlaybackInteractionCoordinator
{
    public PlaybackState ShowControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = true,
            CurrentOverlay = OverlayKind.None
        };

    public PlaybackState HideControls(PlaybackState state) =>
        state with
        {
            AreControlsVisible = false,
            CurrentOverlay = OverlayKind.None
        };

    public PlaybackState ToggleOverlay(PlaybackState state, OverlayKind overlay) =>
        state.CurrentOverlay == overlay
            ? state with { CurrentOverlay = OverlayKind.None }
            : state with { CurrentOverlay = overlay, AreControlsVisible = true };
}
```

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlayerBackend _backend;
    private readonly PlaybackInteractionCoordinator _coordinator;

    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Initial;

    [ObservableProperty]
    private string _urlText = string.Empty;

    public PlayerViewModel(IPlayerBackend backend, PlaybackInteractionCoordinator coordinator)
    {
        _backend = backend;
        _coordinator = coordinator;
    }

    [RelayCommand]
    private async Task OpenUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        await _backend.LoadUrlAsync(UrlText.Trim(), CancellationToken.None);
        State = State with { CurrentUrl = UrlText.Trim(), AreControlsVisible = true };
    }

    [RelayCommand]
    private void ToggleOsd() => State = _coordinator.ToggleOverlay(State, OverlayKind.Osd);

    [RelayCommand]
    private void ShowControls() => State = _coordinator.ShowControls(State);

    public async Task HandleDragAsync(double deltaX, double deltaY)
    {
        var gesture = new GestureDecisionEngine().Classify(deltaX, deltaY);

        if (gesture == PlayerGesture.Seek)
        {
            await _backend.SeekAsync(deltaX > 0 ? 10 : -10, CancellationToken.None);
            return;
        }

        if (gesture == PlayerGesture.Volume)
        {
            var nextVolume = Math.Clamp(State.Volume + (deltaY < 0 ? 5 : -5), 0, 100);
            await _backend.SetVolumeAsync(nextVolume, CancellationToken.None);
            State = State with { Volume = nextVolume, AreControlsVisible = true };
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter PlaybackInteractionCoordinatorTests
```

Expected: PASS

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.App tests/MpvShell.App.Tests
git commit -m "feat: add playback interaction coordinator"
```

### Task 6: 搭建播放器页面、底部大控件和 URL 入口

**Files:**
- Create: `src/MpvShell.App/App.xaml`
- Create: `src/MpvShell.App/App.xaml.cs`
- Create: `src/MpvShell.App/MainWindow.xaml`
- Create: `src/MpvShell.App/MainWindow.xaml.cs`
- Create: `src/MpvShell.App/Views/PlayerPage.xaml`
- Create: `src/MpvShell.App/Views/PlayerPage.xaml.cs`
- Create: `src/MpvShell.App/Styles/PlayerStyles.xaml`

- [x] **Step 1: 写失败测试，先固定 URL 打开命令的最小行为**

```csharp
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class PlayerViewModelOpenUrlTests
{
    [Fact]
    public async Task Open_url_should_store_current_url_and_show_controls()
    {
        var vm = new PlayerViewModel(new FakeBackend(), new PlaybackInteractionCoordinator())
        {
            UrlText = "https://example.com/master.m3u8"
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.State.CurrentUrl.Should().Be("https://example.com/master.m3u8");
        vm.State.AreControlsVisible.Should().BeTrue();
    }

    private sealed class FakeBackend : IPlayerBackend
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());
        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));
        public async IAsyncEnumerable<MpvShell.Player.Abstractions.Events.PlayerEvent> ObserveEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { yield break; }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter PlayerViewModelOpenUrlTests
```

Expected: FAIL，报 `OpenUrlCommand` 不可访问或行为未满足预期。

- [x] **Step 3: 完成前端壳层的最小页面实现**

```xml
<!-- src/MpvShell.App/MainWindow.xaml -->
<Window
    x:Class="MpvShell.App.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:views="using:MpvShell.App.Views">
    <views:PlayerPage />
</Window>
```

```csharp
// src/MpvShell.App/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.MpvSidecar;

namespace MpvShell.App;

public partial class App : Application
{
    public IServiceProvider Services { get; }

    public App()
    {
        this.InitializeComponent();

        var services = new ServiceCollection();
        services.AddSingleton<IPlayerBackend, MpvSidecarBackend>();
        services.AddSingleton<PlaybackInteractionCoordinator>();
        services.AddSingleton<PlayerViewModel>();
        Services = services.BuildServiceProvider();
    }
}
```

```xml
<!-- src/MpvShell.App/Views/PlayerPage.xaml -->
<Page
    x:Class="MpvShell.App.Views.PlayerPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:interop="using:MpvShell.Interop.VideoHost"
    xmlns:models="using:MpvShell.Player.Abstractions.Models">
    <Grid x:Name="RootGrid" Background="#0B1220">
        <interop:VideoHostControl x:Name="VideoHost" />

        <Grid VerticalAlignment="Top" Margin="24">
            <StackPanel Orientation="Horizontal" Spacing="12">
                <TextBox Width="520"
                         PlaceholderText="粘贴 HTTP / HTTPS / m3u8 地址"
                         Text="{Binding UrlText, Mode=TwoWay}" />
                <Button Content="播放" Height="48" MinWidth="96" Command="{Binding OpenUrlCommand}" />
            </StackPanel>
        </Grid>

        <Border VerticalAlignment="Bottom"
                Margin="24"
                Padding="20"
                Background="#CC111827"
                CornerRadius="20">
            <StackPanel Spacing="16">
                <ProgressBar Height="12"
                             Minimum="0"
                             Maximum="{Binding State.DurationSeconds}"
                             Value="{Binding State.PositionSeconds}" />
                <StackPanel Orientation="Horizontal" Spacing="12">
                    <Button Content="-15s" MinWidth="120" Height="56" />
                    <Button Content="播放 / 暂停" MinWidth="160" Height="56" />
                    <Button Content="+30s" MinWidth="120" Height="56" />
                    <Button Content="字幕 / 音轨" MinWidth="160" Height="56" />
                    <Button Content="OSD" MinWidth="120" Height="56" Command="{Binding ToggleOsdCommand}" />
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</Page>
```

```csharp
// src/MpvShell.App/Views/PlayerPage.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using MpvShell.App.ViewModels;

namespace MpvShell.App.Views;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }

    public PlayerPage()
    {
        this.InitializeComponent();
        ViewModel = ((App)Application.Current).Services.GetRequiredService<PlayerViewModel>();
        DataContext = ViewModel;
    }
}
```

- [ ] **Step 4: 运行测试并手工验证页面**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter PlayerViewModelOpenUrlTests
```

Expected: PASS

Run:

```bash
dotnet build MpvShell.slnx -v minimal
dotnet run --project src/MpvShell.App/MpvShell.App.csproj
```

Expected: 能看到视频宿主占位区、URL 输入框和底部大控件。

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.App
git commit -m "feat: build player shell page"
```

### Task 7: 实现时间轴拖拽、横屏手势和自动隐藏

**Files:**
- Create: `src/MpvShell.App/Services/GestureDecisionEngine.cs`
- Modify: `src/MpvShell.App/Services/PlaybackInteractionCoordinator.cs`
- Modify: `src/MpvShell.App/ViewModels/PlayerViewModel.cs`
- Modify: `src/MpvShell.App/Views/PlayerPage.xaml`
- Create: `tests/MpvShell.App.Tests/GestureDecisionEngineTests.cs`

- [x] **Step 1: 写失败测试，固定 seek 手势和自动隐藏判定**

```csharp
using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class GestureDecisionEngineTests
{
    [Fact]
    public void Horizontal_drag_should_be_classified_as_seek()
    {
        var engine = new GestureDecisionEngine();

        var gesture = engine.Classify(deltaX: 120, deltaY: 10);

        gesture.Should().Be(PlayerGesture.Seek);
    }
}
```

```csharp
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class AutoHideTests
{
    [Fact]
    public void Idle_timeout_should_hide_controls_when_no_overlay_is_open()
    {
        var coordinator = new PlaybackInteractionCoordinator();
        var state = PlaybackState.Initial with { AreControlsVisible = true };

        var next = coordinator.OnIdleTimeout(state);

        next.AreControlsVisible.Should().BeFalse();
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter "GestureDecisionEngineTests|AutoHideTests"
```

Expected: FAIL，报 `GestureDecisionEngine` 或 `OnIdleTimeout` 未定义。

- [x] **Step 3: 写最小实现，建立手势与自动隐藏行为**

```csharp
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
```

```csharp
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Services;

public sealed partial class PlaybackInteractionCoordinator
{
    public PlaybackState OnIdleTimeout(PlaybackState state) =>
        state.CurrentOverlay == OverlayKind.None
            ? state with { AreControlsVisible = false }
            : state;
}
```

```xml
<!-- 在 PlayerPage.xaml 的视频层上补输入事件 -->
<Grid Background="#0B1220"
      PointerPressed="OnVideoPointerPressed"
      PointerMoved="OnVideoPointerMoved">
```

```xml
<!-- 把底部进度条从 ProgressBar 改成可拖拽的 Slider -->
<Slider Minimum="0"
        Maximum="{Binding State.DurationSeconds}"
        Value="{Binding State.PositionSeconds, Mode=TwoWay}"
        Header="播放进度"
        ManipulationCompleted="OnTimelineManipulationCompleted" />
```

```csharp
// src/MpvShell.App/Views/PlayerPage.xaml.cs
private Point? _dragStartPoint;

private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
{
    _dragStartPoint = e.GetCurrentPoint((UIElement)sender).Position;
    ViewModel.ShowControlsCommand.Execute(null);
}

private async void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (_dragStartPoint is null || !e.GetCurrentPoint((UIElement)sender).IsInContact)
    {
        return;
    }

    var current = e.GetCurrentPoint((UIElement)sender).Position;
    await ViewModel.HandleDragAsync(current.X - _dragStartPoint.Value.X, current.Y - _dragStartPoint.Value.Y);
}

private async void OnTimelineManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
{
    if (sender is Slider slider)
    {
        await ViewModel.SeekToAsync(slider.Value);
    }
}
```

```csharp
// PlayerViewModel 中补时间轴拖拽入口
public async Task SeekToAsync(double seconds)
{
    await _backend.SetPositionAsync(seconds, CancellationToken.None);
    State = State with { PositionSeconds = seconds, AreControlsVisible = true };
}
```

- [ ] **Step 4: 运行测试并手工验证**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter "GestureDecisionEngineTests|AutoHideTests"
```

Expected: PASS

Run:

```bash
dotnet run --project src/MpvShell.App/MpvShell.App.csproj
```

Expected: 点击画面会呼出控件；横向拖动会走 seek 分支；空闲后控件会自动隐藏。

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.App tests/MpvShell.App.Tests
git commit -m "feat: add gestures and auto-hide behavior"
```

### Task 8: 实现 OSD、字幕/音轨面板、详细信息面板与最近 URL

**Files:**
- Create: `src/MpvShell.App/ViewModels/InfoPanelViewModel.cs`
- Create: `src/MpvShell.App/Services/RecentUrlStore.cs`
- Modify: `src/MpvShell.App/ViewModels/PlayerViewModel.cs`
- Modify: `src/MpvShell.App/Views/PlayerPage.xaml`
- Create: `tests/MpvShell.App.Tests/RecentUrlStoreTests.cs`

- [ ] **Step 1: 写失败测试，锁定最近 URL 去重和信息面板格式**

```csharp
using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class RecentUrlStoreTests
{
    [Fact]
    public void Add_should_move_duplicate_url_to_front()
    {
        var store = new RecentUrlStore();

        store.Add("https://a.example/1.m3u8");
        store.Add("https://b.example/2.m3u8");
        store.Add("https://a.example/1.m3u8");

        store.Items[0].Should().Be("https://a.example/1.m3u8");
        store.Items.Should().HaveCount(2);
    }
}
```

```csharp
using FluentAssertions;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class InfoPanelViewModelTests
{
    [Fact]
    public void Snapshot_should_format_summary_lines()
    {
        var vm = new InfoPanelViewModel();
        vm.Update(new InfoPanelSnapshot("hevc", "eac3", "HDR10", "3840x2160", "10-bit", "23.976", "forward=8s"));

        vm.VideoSummary.Should().Contain("hevc");
        vm.VideoSummary.Should().Contain("3840x2160");
        vm.HdrSummary.Should().Contain("HDR10");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter "RecentUrlStoreTests|InfoPanelViewModelTests"
```

Expected: FAIL，报 `RecentUrlStore` 或 `InfoPanelViewModel` 未定义。

- [ ] **Step 3: 写最小实现，并把 OSD/信息面板接入页面**

```csharp
namespace MpvShell.App.Services;

public sealed class RecentUrlStore
{
    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;

    public void Add(string url)
    {
        _items.Remove(url);
        _items.Insert(0, url);

        if (_items.Count > 10)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }
}
```

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.ViewModels;

public partial class InfoPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _videoSummary = string.Empty;

    [ObservableProperty]
    private string _audioSummary = string.Empty;

    [ObservableProperty]
    private string _hdrSummary = string.Empty;

    public void Update(InfoPanelSnapshot snapshot)
    {
        VideoSummary = $"{snapshot.VideoCodec} | {snapshot.Resolution} | {snapshot.BitDepth} | {snapshot.FrameRate}";
        AudioSummary = $"{snapshot.AudioCodec} | {snapshot.CacheState}";
        HdrSummary = snapshot.HdrType ?? "SDR / Unknown";
    }
}
```

```xml
<!-- 在 PlayerPage.xaml 追加信息面板、轨道面板和 OSD 浮层 -->
<Border HorizontalAlignment="Right"
        VerticalAlignment="Top"
        Margin="24"
        Padding="16"
        Background="#E5111827"
        CornerRadius="16"
        Visibility="{Binding InfoPanelVisibility}">
    <StackPanel Spacing="8">
        <TextBlock Text="详细信息" FontSize="20" FontWeight="SemiBold" />
        <TextBlock Text="{Binding InfoPanel.VideoSummary}" />
        <TextBlock Text="{Binding InfoPanel.AudioSummary}" />
        <TextBlock Text="{Binding InfoPanel.HdrSummary}" />
    </StackPanel>
</Border>

<Border HorizontalAlignment="Left"
        VerticalAlignment="Bottom"
        Margin="24,24,24,140"
        Padding="16"
        Background="#E50F172A"
        CornerRadius="16"
        Visibility="{Binding TracksVisibility}">
    <StackPanel Spacing="12">
        <TextBlock Text="字幕 / 音轨" FontSize="20" FontWeight="SemiBold" />
        <ItemsControl ItemsSource="{Binding Tracks}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="models:TrackInfo">
                    <Button Content="{x:Bind Title}" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>

<Border HorizontalAlignment="Right"
        VerticalAlignment="Bottom"
        Margin="24,24,24,140"
        Padding="16"
        Background="#E51F2937"
        CornerRadius="16"
        Visibility="{Binding OsdVisibility}">
    <StackPanel Spacing="12">
        <Button Content="字幕 / 音轨" Command="{Binding ToggleTracksCommand}" />
        <Button Content="详细信息" Command="{Binding ToggleInfoPanelCommand}" />
        <ItemsControl ItemsSource="{Binding RecentUrls}" />
    </StackPanel>
</Border>
```

```csharp
// PlayerViewModel 中补最近 URL 和信息面板
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

public InfoPanelViewModel InfoPanel { get; } = new();
private readonly RecentUrlStore _recentUrlStore = new();
public ObservableCollection<TrackInfo> Tracks { get; } = new();
public IReadOnlyList<string> RecentUrls => _recentUrlStore.Items;
public Visibility InfoPanelVisibility => State.CurrentOverlay == OverlayKind.InfoPanel ? Visibility.Visible : Visibility.Collapsed;
public Visibility OsdVisibility => State.CurrentOverlay == OverlayKind.Osd ? Visibility.Visible : Visibility.Collapsed;
public Visibility TracksVisibility => State.CurrentOverlay == OverlayKind.Tracks ? Visibility.Visible : Visibility.Collapsed;

partial void OnStateChanged(PlaybackState value)
{
    OnPropertyChanged(nameof(InfoPanelVisibility));
    OnPropertyChanged(nameof(OsdVisibility));
    OnPropertyChanged(nameof(TracksVisibility));
}

private async Task OpenUrlAsync()
{
    if (string.IsNullOrWhiteSpace(UrlText))
    {
        return;
    }

    var url = UrlText.Trim();
    await _backend.LoadUrlAsync(url, CancellationToken.None);
    _recentUrlStore.Add(url);
    InfoPanel.Update(await _backend.GetInfoSnapshotAsync(CancellationToken.None));
    Tracks.Clear();
    foreach (var track in await _backend.GetTracksAsync(CancellationToken.None))
    {
        Tracks.Add(track);
    }
    State = State with { CurrentUrl = url, AreControlsVisible = true };
    OnPropertyChanged(nameof(RecentUrls));
}

[RelayCommand]
private void ToggleTracks() => State = _coordinator.ToggleOverlay(State, OverlayKind.Tracks);

[RelayCommand]
private void ToggleInfoPanel() => State = _coordinator.ToggleOverlay(State, OverlayKind.InfoPanel);
```

- [ ] **Step 4: 运行测试并执行 V1 手工验收**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter "RecentUrlStoreTests|InfoPanelViewModelTests"
dotnet build MpvShell.slnx -v minimal
```

Expected: PASS / BUILD SUCCEEDED

Manual check:

```bash
dotnet run --project src/MpvShell.App/MpvShell.App.csproj
```

Expected:

- 粘贴 URL 可开始播放
- 底部大控件可操作
- OSD 可打开
- 能看到详细信息面板
- 最近 URL 能保留去重顺序

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.App tests/MpvShell.App.Tests
git commit -m "feat: add osd info panel and recent urls"
```

### Task 9: 收口错误处理、日志和 V1 验收脚本

**Files:**
- Modify: `src/MpvShell.Player.MpvSidecar/MpvSidecarBackend.cs`
- Modify: `src/MpvShell.App/ViewModels/PlayerViewModel.cs`
- Create: `tests/MpvShell.App.Tests/ErrorPresentationTests.cs`
- Create: `docs/manual-test-checklist.md`

- [x] **Step 1: 写失败测试，锁定后端错误向 UI 冒泡的行为**

```csharp
using FluentAssertions;
using MpvShell.App.Services;
using MpvShell.App.ViewModels;
using MpvShell.Player.Abstractions;
using MpvShell.Player.Abstractions.Models;

namespace MpvShell.App.Tests;

public sealed class ErrorPresentationTests
{
    [Fact]
    public async Task Open_url_should_store_error_message_when_backend_throws()
    {
        var vm = new PlayerViewModel(new ThrowingBackend(), new PlaybackInteractionCoordinator())
        {
            UrlText = "https://broken.example/stream.m3u8"
        };

        await vm.OpenUrlCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("无法连接到 mpv IPC");
    }

    private sealed class ThrowingBackend : IPlayerBackend
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadUrlAsync(string url, CancellationToken cancellationToken) => throw new InvalidOperationException("无法连接到 mpv IPC");
        public Task PlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SeekAsync(double deltaSeconds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetPositionAsync(double absoluteSeconds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetMuteAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<TrackInfo>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrackInfo>>(Array.Empty<TrackInfo>());
        public Task SetAudioTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetSubtitleTrackAsync(int trackId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InfoPanelSnapshot> GetInfoSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new InfoPanelSnapshot(null, null, null, null, null, null, null));
        public async IAsyncEnumerable<MpvShell.Player.Abstractions.Events.PlayerEvent> ObserveEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { yield break; }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```bash
dotnet test tests/MpvShell.App.Tests/MpvShell.App.Tests.csproj -v minimal --filter ErrorPresentationTests
```

Expected: FAIL，报 `ErrorMessage` 未定义，或 `OpenUrlAsync` 未把异常转成 UI 错误状态。

- [x] **Step 3: 写最小实现，补全错误通路和人工验收清单**

```csharp
// MpvSidecarBackend.cs 中补异常保护
public async Task InitializeAsync(nint hostHandle, CancellationToken cancellationToken)
{
    try
    {
        var pipeName = $"mpvshell-{Environment.ProcessId}";
        var options = new MpvLaunchOptions("mpv.exe", pipeName, hostHandle);
        _processManager.Start(options);
        await _ipcClient.ConnectAsync(pipeName, cancellationToken);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("初始化 mpv 后端失败", ex);
    }
}
```

```csharp
// PlayerViewModel 中补用户可见错误
[ObservableProperty]
private string? _errorMessage;

private async Task OpenUrlAsync()
{
    try
    {
        if (string.IsNullOrWhiteSpace(UrlText))
        {
            return;
        }

        var url = UrlText.Trim();
        await _backend.LoadUrlAsync(url, CancellationToken.None);
        _recentUrlStore.Add(url);
        InfoPanel.Update(await _backend.GetInfoSnapshotAsync(CancellationToken.None));
        State = State with { CurrentUrl = url, AreControlsVisible = true };
        ErrorMessage = null;
    }
    catch (Exception ex)
    {
        ErrorMessage = ex.Message;
    }
}
```

```md
# V1 手工验收清单

- [ ] 能粘贴 HTTP/HTTPS 直链并播放
- [ ] 能粘贴 m3u8 并播放
- [ ] 点击画面会呼出控件
- [ ] 空闲后控件会自动隐藏
- [ ] 底部进度条支持拖拽
- [ ] 横向拖动会触发 seek
- [ ] 可打开字幕/音轨面板
- [ ] 可打开详细信息面板
- [ ] 异常 URL 会给出错误提示
- [ ] 退出应用时 mpv 子进程被清理
```

- [ ] **Step 4: 运行全量验证**

Run:

```bash
dotnet test MpvShell.slnx -v minimal
dotnet build MpvShell.slnx -v minimal
```

Expected: 所有测试 PASS，且 BUILD SUCCEEDED

> 当前会话已完成等价自动化验证（全部测试项目通过 + App 项目构建成功）；因会话环境无法执行真实交互，V1 清单的人机交互项待本地手工逐条勾选。

Manual check:

```bash
dotnet run --project src/MpvShell.App/MpvShell.App.csproj
```

Expected: V1 验收清单各项可逐条勾选。

- [ ] **Step 5: 提交**

```bash
git add src/MpvShell.Player.MpvSidecar src/MpvShell.App tests docs/manual-test-checklist.md
git commit -m "feat: finalize v1 validation and error handling"
```
