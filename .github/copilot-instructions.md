# Copilot 项目指令

本仓库是 **mpv-winui**，应用项目当前使用 `MpvShell` 命名空间。它是仅面向 Windows 的 WinUI 3 播放器，以 libmpv 提供媒体处理和视频渲染能力，以 WinUI 3 提供触屏、键鼠、OSD 和信息界面。

`docs/architecture.md` 是技术选型、模块边界和实施顺序的唯一架构基线。代码与本文冲突时，以该文档为准；不要根据仓库中的遗留实现推断正式架构。

## 语言与沟通

- 文档、注释、提交说明、异常消息均使用**中文**（代码中已有中文注释和中文异常消息，保持一致）。
- 代码标识符（类名、方法名等）保持英文。

## 工作方式

- 复杂改动先核对 `docs/architecture.md`，说明结论、关键依据、风险和验证结果。
- 简单修改直接给出精简结果，不展开无关过程。
- 不确定的原生 API、线程约束或资源所有权必须先查证，再实现。

## 技术栈

- **运行时**：.NET 10 LTS，`ImplicitUsings` + `Nullable` 均开启（见 `Directory.Build.props`）。
- **UI**：WinUI 3 / Windows App SDK 2.4.0（`net10.0-windows10.0.19041.0`，最低平台 `10.0.17763.0`），`UseWinUI`。
- **MVVM**：CommunityToolkit.Mvvm（8.4.0）。
- **DI**：Microsoft.Extensions.DependencyInjection，在 `App` 构造函数中注册。
- **测试**：xUnit + FluentAssertions，断言统一使用 FluentAssertions 的 `Should()` 风格。
- **首发架构**：仅支持 x64；不得擅自增加 x86 或 ARM64 发布目标。

## 解决方案结构

唯一解决方案文件为 `mpv-winui.slnx`（`.slnx` 格式，不是 `.sln`）。所有构建、测试和项目增删都以该文件为入口。

正式目标结构如下：

```
src/
  MpvShell.App                    # WinUI 3 表现层（ViewModels / Views / Services）
  MpvShell.Player.Abstractions    # 播放器抽象层（IPlayerBackend、事件、统一状态模型）
  MpvShell.Player.LibMpv          # libmpv 会话、命令、属性、事件和 C API 互操作
  MpvShell.Rendering.WinUI        # SwapChainPanel、D3D11、ANGLE/EGL 和渲染线程
tests/
  MpvShell.App.Tests
  MpvShell.Player.Abstractions.Tests
  MpvShell.Player.LibMpv.Tests
  MpvShell.Rendering.WinUI.Tests
```

依赖方向：`App → Player.Abstractions`、`App → Rendering.WinUI`、`Player.LibMpv → Player.Abstractions`，且控制后端与渲染器共享同一份 mpv core。`Player.Abstractions` 不得引用 WinUI、ANGLE、D3D11 或 libmpv。

仓库当前仍可能存在 `MpvShell.Player.MpvSidecar`、`MpvShell.Interop.VideoHost` 及其测试。这些是待迁移的旧路线，不是新功能的落点，不得继续扩展；完成对应替代后再安全移除。

## 架构规则（必须遵守）

1. **固定播放路线**：使用进程内 `libmpv`、C Client API 和 Render API。禁止启动外部 `mpv.exe`、使用命名管道 JSON IPC、以 `--wid` 子窗口承载最终视频，或同时维护 Sidecar 与 libmpv 两套正式后端。
2. **抽象边界**：前端和交互协调层只能通过 `IPlayerBackend` 访问播放控制；UI 不接触 mpv 原生句柄。视频表面由独立渲染接口管理，`IPlayerBackend` 不接收裸 `HWND`。
3. **状态统一**：Abstractions 层返回应用自己的强类型状态与事件，不把 mpv 原始属性名、结构体或指针暴露给 UI。
4. **渲染链路**：使用 libmpv Render API，经 ANGLE/EGL 接入 D3D11 Composition SwapChain，并由 WinUI 3 `SwapChainPanel` 承载。XAML 视频层之上必须能稳定叠加交互控件。
5. **线程边界**：固定 UI、mpv 事件/命令、渲染三个逻辑执行域。回调只发送轻量唤醒信号；禁止同步循环等待。EGL/OpenGL 上下文由渲染线程独占，`SetSwapChain` 在 UI 线程调用。
6. **资源所有权**：render context 必须先于 mpv core 释放。原生资源使用 `SafeHandle` 或明确所有权对象；回调注销前必须保持委托或函数指针有效，托管异常不得跨越原生 ABI。
7. **交互逻辑集中**：手势冲突、浮层优先级、seek/音量节流等逻辑放在协调服务中，不散落在 XAML code-behind。
8. **Phase 0 优先**：在 libmpv 控制、SDR 渲染、XAML 覆盖输入、4K HDR 与硬件解码全部通过前，不堆叠完整产品 UI。不得用 UI 假数据、CPU 逐帧拷贝或外部 mpv 进程绕过失败项。

## 文件编码（必须遵守）

- 仓库中的所有文本文件统一使用 **UTF-8（无 BOM）**；修改文件时必须保留 UTF-8，禁止使用 ANSI、GBK、系统默认代码页或 `Encoding.Default` 读写。
- `mpv-winui.slnx` 是 XML 文件且未声明其他编码，因此必须始终保存为 UTF-8。不得用默认编码的 PowerShell、脚本或重定向命令重写该文件；写入中文“解决方案项”等内容时尤其要显式指定 UTF-8。
- 修改 `.slnx` 后必须运行 `dotnet sln mpv-winui.slnx list`，确认文件可被严格解析；涉及项目结构时还须运行解决方案构建。若出现乱码、替换字符 `�` 或 `Invalid character in the given encoding`，不得提交或继续操作，必须先恢复正确的 UTF-8 内容。

## 编码约定

- 异步方法以 `Async` 结尾，所有可取消的公共方法接受 `CancellationToken`。
- 失败路径保留原始异常作为内部异常，用户可见消息使用中文，并区分加载、播放、渲染和不可恢复初始化错误。
- 后端、会话和资源所有权类型优先使用 `sealed`，按实际生命周期实现 `IDisposable` 或 `IAsyncDisposable`。
- 固定原生入口优先使用源生成的 `LibraryImport`；仅在不适用时使用 `DllImport`。显式处理 UTF-8、结构体布局、调用约定、指针宽度和非托管内存释放。
- 使用稳定逻辑库名和 `NativeLibrary.SetDllImportResolver` 从固定 RID 目录加载经过哈希校验的 DLL；不得修改进程级 DLL 搜索路径，也不得从当前工作目录或 `PATH` 隐式加载。
- libmpv 命令使用结构化参数和异步 API，不拼接命令字符串。每个请求使用唯一 `reply_userdata` 关联回复、错误和超时。
- 默认不加载用户 mpv 配置、脚本、插件或 ytdl；URL 只允许 `http`/`https`，不得进入 shell 或命令行，日志必须对查询参数和令牌脱敏。

## 测试约定

- 每个 `src` 项目对应一个 `tests` 项目，测试类名以被测对象命名并加 `Tests` 后缀。
- 新增/修改 Services、ViewModels、后端解析器等逻辑时，同步补充或更新对应测试。
- 断言用 FluentAssertions（`Should()`），不混用 `Assert`。
- 纯逻辑（命令构造、事件解析、手势决策、URL 历史等）优先做成可独立于 WinUI 运行时测试的形式。
- 原生边界必须覆盖 ABI 布局、枚举值、UTF-8、回调寿命、错误码、异步请求关联和重复释放；发布产物必须执行 x64 DLL 加载、会话创建与销毁烟雾测试。
- 渲染测试至少覆盖初始化、resize、DPI、关闭和设备重建；HDR 与硬件解码结论必须来自真实硬件验证，不能只根据配置名称或单元测试判断。

## 运行与构建

- 在 **Windows PowerShell 5.1** 中运行或捕获 `dotnet` 输出前，必须先在当前终端会话执行一次以下临时 UTF-8 初始化，防止管道（例如 `2>&1 | Select-Object`）把 UTF-8 输出按 GBK 解码成乱码。此设置只作用于当前会话；不要为此修改用户的 PowerShell Profile 或系统级配置。

  ```powershell
  chcp 65001 > $null
  $utf8 = [System.Text.UTF8Encoding]::new($false)
  [Console]::InputEncoding = $utf8
  [Console]::OutputEncoding = $utf8
  $OutputEncoding = $utf8
  ```

- PowerShell 7 不需要上述初始化。无法确认版本时先检查 `$PSVersionTable.PSVersion`；同一 PowerShell 5.1 会话中不要在每条测试命令前重复执行。
- 查看项目：`dotnet sln mpv-winui.slnx list`
- 构建：`dotnet build mpv-winui.slnx -p:Platform=x64`
- 测试：`dotnet test mpv-winui.slnx -p:Platform=x64`
- V1 只生成和验证 `win-x64` 发布产物。原生依赖固定版本、架构和 SHA-256，不使用浮动版本或运行时在线下载。

## 参考文档

- 架构、范围、实施路线与验收标准：`docs/architecture.md`

## 范围提醒（避免过度实现）

V1 不做：媒体库、刮削和海报墙、商业 DRM、浏览器认证与 Cookie 管理界面、跨设备同步、在线脚本市场、多播放内核切换，以及运行时在线下载或替换 libmpv。实现功能前先核对 `docs/architecture.md` 的产品范围和当前 Phase。

# Network access

- This development environment is located in mainland China and has a local HTTP proxy at `http://127.0.0.1:7890`.
- When a shell command needs to search or access GitHub or another overseas website, route that command through the local proxy.
- Prefer per-command proxy settings. Do not modify the user's global Git, shell, or system proxy configuration unless explicitly requested.
- For `curl.exe`, use `--proxy http://127.0.0.1:7890`.
- For Git network operations, use `git -c http.proxy=http://127.0.0.1:7890 <command>`.
- For tools that honor proxy environment variables, set both `HTTP_PROXY` and `HTTPS_PROXY` to `http://127.0.0.1:7890` for that command or shell session.
- Do not proxy localhost, private-network, or domestic-site requests unless the direct request fails and proxying is necessary.
