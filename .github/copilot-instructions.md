# Copilot 项目指令

本项目是 **MpvShell**：基于 WinUI 3 + mpv 的 Windows 播放器壳层（详见 `docs/superpowers/specs/2026-04-07-winui3-mpv-player-shell-design.md`）。目标是保留 mpv 的高清/HDR 播放能力，同时提供现代化的触屏与桌面交互。

## 语言与沟通

- 文档、注释、提交说明、异常消息均使用**中文**（代码中已有中文注释和中文异常消息，保持一致）。
- 代码标识符（类名、方法名等）保持英文。

## 思考深度分级

- 针对复杂逻辑请使用详细的 step-by-step 深度思考推导。
- 简单代码补全和解释请直接给出精简答案，无需长篇思考过程。

## 技术栈

- **运行时**：.NET 10 LTS，`ImplicitUsings` + `Nullable` 均开启（见 `Directory.Build.props`）。
- **UI**：WinUI 3 / Windows App SDK 2.4.0（`net10.0-windows10.0.19041.0`，最低平台 `10.0.17763.0`），`UseWinUI`。
- **MVVM**：CommunityToolkit.Mvvm（8.4.0）。
- **DI**：Microsoft.Extensions.DependencyInjection，在 `App` 构造函数中注册。
- **测试**：xUnit + FluentAssertions，断言统一使用 FluentAssertions 的 `Should()` 风格。

## 解决方案结构

解决方案文件为 `MpvShell.slnx`（slnx 格式，不是 .sln）。

```
src/
  MpvShell.App                    # WinUI 3 表现层（ViewModels / Views / Services）
  MpvShell.Interop.VideoHost      # 原生互操作层（VideoHostControl、P/Invoke、窗口句柄/边界转换）
  MpvShell.Player.Abstractions    # 播放器抽象层（IPlayerBackend、事件、统一状态模型）
  MpvShell.Player.MpvSidecar      # V1 后端：mpv.exe Sidecar + JSON IPC
tests/
  MpvShell.App.Tests
  MpvShell.Interop.VideoHost.Tests
  MpvShell.Player.Abstractions.Tests
  MpvShell.Player.MpvSidecar.Tests
```

依赖方向：`App → MpvSidecar → Abstractions`；`App → Interop.VideoHost`。Abstractions 不依赖任何其他项目。

## 架构规则（必须遵守）

1. **可替换后端设计**：前端和交互协调层只能通过 `IPlayerBackend`（`Player.Abstractions`）访问播放器，禁止在 UI 或协调层直接引用 `MpvSidecarBackend`（DI 注册除外）。这是为未来切换到 `libmpv` 预留的路径。
2. **状态统一**：Abstractions 层返回应用自己的状态模型（`PlaybackState`、`InfoPanelSnapshot`、`TrackInfo`、`PlayerEvent`），不要把 mpv 原始 IPC 数据直接暴露给前端。
3. **交互逻辑集中**：手势冲突、浮层显隐/优先级、seek/音量节流等逻辑放在 `PlaybackInteractionCoordinator` / `GestureDecisionEngine` 等 Services 中，不要散落在 XAML code-behind 或 View 里。
4. **code-behind 保持轻薄**：XAML code-behind 只做初始化与事件转发，业务逻辑放 ViewModel/Services。

## 编码约定

- 异步方法以 `Async` 结尾，所有可取消的公共方法接受 `CancellationToken`。
- 失败路径抛出 `InvalidOperationException` 等异常并在 `catch (Exception ex)` 中包装原始异常（`throw new InvalidOperationException("初始化 mpv 后端失败", ex)` 风格），消息使用中文。
- 后端/管理器类使用 `sealed`，优先实现 `IAsyncDisposable` 管理进程/IPC 资源。
- 进程、句柄、IPC 连接等资源的获取与清理必须成对出现（参考 `MpvProcessManager` / `MpvJsonIpcClient` 的做法）。
- 不要在代码中硬编码管道名、可执行文件名等运行时参数，走 `MpvLaunchOptions` 这类配置载体。

## 测试约定

- 每个 `src` 项目对应一个 `tests` 项目，测试类名以被测对象命名 + `Tests` 后缀（如 `MpvCommandFactoryTests`）。
- 新增/修改 Services、ViewModels、后端解析器等逻辑时，同步补充或更新对应测试。
- 断言用 FluentAssertions（`Should()`），不混用 `Assert`。
- 纯逻辑（命令构造、事件解析、手势决策、URL 历史等）优先做成可独立于 WinUI 运行时测试的形式。

## 运行与构建

- 构建：`dotnet build MpvShell.slnx`
- 测试：`dotnet test MpvShell.slnx`
- 发布配置见 `Properties/PublishProfiles/`（win-x64 / win-arm64 / win-x86）。

## 参考文档

- 设计文档：`docs/superpowers/specs/2026-04-07-winui3-mpv-player-shell-design.md`（范围内外、7 项最高优先级交互需求、四层架构说明）
- 实施计划：`docs/superpowers/plans/2026-04-07-winui3-mpv-player-shell-v1.md`
- 手动测试清单：`docs/manual-test-checklist.md`

## 范围提醒（避免过度实现）

V1 不做：媒体库、元数据刮削、DRM、浏览器认证、跨设备同步、亮度手势。实现功能前先对照设计文档的"范围内/范围外"清单。
