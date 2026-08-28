# Phase 0 技术可行性实施计划

> 状态：待执行  
> 架构基线：`docs/architecture.md` v1.1  
> 编制日期：2026-08-28  
> 适用范围：Phase 0A～0D，不包含完整 V1 产品功能

## 1. 目标

本计划把 `docs/architecture.md` 中已经选定的技术路线拆成可由 AI Agent 逐项实施、逐项验证、逐项提交的工作包。

Phase 0 的最终目标不是完成播放器产品，而是用真实代码、真实 libmpv、真实视频和真实硬件证明以下结论：

- 应用不启动外部 `mpv.exe`，能够在进程内稳定创建、控制和销毁 libmpv 会话。
- 视频通过 libmpv Render API、ANGLE/EGL 和 D3D11 Composition SwapChain 显示在 WinUI 3 `SwapChainPanel` 中。
- XAML 控件可以稳定覆盖视频并接收触摸、鼠标和键盘输入。
- resize、DPI、全屏、显示器切换和重复关闭/重建不会产生稳定复现的黑屏、错位、死锁或原生资源泄漏。
- 4K HEVC Main10、AV1、硬件解码、SDR 和 HDR 达到 `docs/architecture.md` 规定的验收标准。

Phase 0 全部通过前，不继续堆叠完整产品 UI，也不把旧 Sidecar 或 `--wid` 方案作为失败时的回退路线。

## 2. 执行约束

### 2.1 Agent 执行规则

- 每次只实施一个工作包；开始前读取本计划、`phase-0-progress.md`、`docs/architecture.md` 和 `.github/copilot-instructions.md`。
- 先检查当前代码与进度记录，避免重复已完成工作或覆盖用户的未提交修改。
- 工作包内允许做完成该目标所需的代码、测试、项目配置和文档更新；不得顺带实施后续工作包。
- 完成代码后运行该工作包列出的最小验证集，并把命令、结果和未验证项写入 `phase-0-progress.md`。
- 自动化验证不能替代人工硬件验证。没有真实证据时必须记为“未验证”，不得推断为通过。
- ABI、ANGLE 行为、HDR 输出或驱动能力不明确时先查官方头文件和文档；仍不明确则停止并记录阻塞，不凭记忆补齐。
- 每个工作包应形成一个可独立审查和回退的提交。提交前保持工作区仅包含本工作包范围内的变更。
- 不修改架构路线。需要偏离 `docs/architecture.md` 时，停止实施并请求决策。

### 2.2 固定技术约束

- 目标框架为 `.NET 10`，Windows 项目使用 `net10.0-windows10.0.19041.0`。
- Windows App SDK 固定为 `2.4.0` Stable；原生依赖使用确定版本，不使用浮动版本。
- Phase 0 和 V1 只支持 `win-x64`，不增加 x86 或 ARM64。
- libmpv 基线固定为 mpv `v0.41.0` 的可审计 x64 构建。
- 使用 libmpv C Client API 和 Render API；禁止外部 `mpv.exe`、JSON IPC、命名管道和最终 `--wid` 承载。
- 使用 D3D11 后端的 ANGLE/EGL/OpenGL；不使用已废弃的 `opengl-cb`。
- 不从当前工作目录或 `PATH` 隐式加载 DLL，不修改进程级 DLL 搜索路径，不在运行时下载原生依赖。
- 原生回调只唤醒对应线程，不在回调中执行重活，不允许托管异常跨越 ABI 边界。
- render context 必须先于 mpv core 释放；从 `SwapChainPanel` 解除 SwapChain 后才能释放图形资源。

## 3. 当前仓库审计

审计日期为 2026-08-28。

| 区域 | 当前状态 | Phase 0 处理方向 |
|---|---|---|
| 解决方案 | `mpv-winui.slnx` 包含 4 个生产项目和 4 个测试项目 | 改为目标项目结构，并补齐可用的 x64 解决方案配置 |
| 目标框架 | 项目已迁移到 .NET 10；WinUI 项目目标为 `net10.0-windows10.0.19041.0` | 保留并统一可重现配置 |
| 播放抽象 | `IPlayerBackend.InitializeAsync` 接收裸 `hostHandle` | 移除旧 `--wid` 细节，拆分播放控制和视频表面职责 |
| 播放后端 | `MpvShell.Player.MpvSidecar` 启动 `mpv.exe` 并使用 JSON IPC | 由 `MpvShell.Player.LibMpv` 完全替代，不继续扩展 |
| 视频承载 | `MpvShell.Interop.VideoHost` 只有子窗口句柄和 `MoveWindow` 旧模型 | 由 `MpvShell.Rendering.WinUI` 和 `SwapChainPanel` 完全替代 |
| 应用组合根 | DI 直接注册 `MpvSidecarBackend` | 最终改为共享一份 `MpvPlayerSession` 的控制后端与渲染器 |
| 页面 | `PlayerPage` 使用 `VideoHostControl`，已有透明交互层和基础 XAML 覆盖 UI | 保留可用交互 UI，将视频底层替换为 `SwapChainPanel` |
| 状态模型 | 已有 `PlaybackState`、`TrackInfo`、`InfoPanelSnapshot` 和少量事件 | 保留概念，按架构补全缓冲、媒体、渲染、结束和错误状态 |
| ViewModel/服务 | 已有播放命令、手势、自动隐藏、最近 URL、信息面板逻辑 | 尽量保留；适配新的无 HWND 抽象和强类型事件 |
| 测试 | 默认配置下 21 个测试通过 | 保留不依赖旧架构的测试；替换 Sidecar/VideoHost 测试并增加 ABI、会话和渲染测试 |
| 原生依赖 | 仓库中没有固定的 libmpv/ANGLE RID 资产、哈希清单或构建来源记录 | 在接入 P/Invoke 前完成来源、版本、架构、哈希和许可证基线 |
| x64 命令 | `dotnet build mpv-winui.slnx -p:Platform=x64` 因缺少 `Debug|x64` 配置失败 | P0-01 修复；修复前以默认配置作为代码基线 |

### 3.1 可保留内容

- `MpvShell.App` 中与视频承载无关的 ViewModel、交互协调、手势判断、最近 URL 和基础 XAML 控件。
- `MpvShell.Player.Abstractions` 中“应用拥有自己的状态模型”这一边界，以及可继续演进的播放状态、轨道和信息模型。
- 不依赖 `MpvSidecarBackend`、JSON IPC、`VideoHostControl` 或裸 HWND 语义的单元测试。
- 当前 .NET 10、Nullable、ImplicitUsings、xUnit 和 FluentAssertions 基线。

### 3.2 必须替换内容

- `IPlayerBackend.InitializeAsync(nint hostHandle, ...)` 及所有向播放控制层传递 HWND 的调用。
- `MpvShell.Player.MpvSidecar`、`MpvProcessManager`、`MpvJsonIpcClient`、JSON 命令和事件解析。
- `MpvShell.Interop.VideoHost`、`VideoHostControl` 和 `MoveWindow` 子窗口承载路径。
- App 对 Sidecar 的 DI 注册，以及 `PlayerPage` 对 `VideoHostControl` 的 XAML 引用。
- 仅轮询外部进程、没有真实 mpv 状态映射的事件实现。

旧项目在替代链路尚未达到对应闸口前可以暂留以保持基线可构建，但不得新增功能；在 P0-11 统一移除，最终解决方案中不得保留两套正式后端。

## 4. 实施前输入与阻塞条件

以下输入必须在相应工作包开始前明确，并记录在 `phase-0-progress.md`：

| 输入 | 最迟需要时间 | 可接受证据 |
|---|---|---|
| mpv `v0.41.0` x64 libmpv 构建来源 | P0-02 前 | 自建脚本与构建参数，或明确来源、完整 DLL 清单、许可证和 SHA-256 |
| 与该构建匹配的 mpv 头文件 | P0-03 前 | 固定到 v0.41.0 的 `client.h`、`render.h`、`render_gl.h` |
| ANGLE x64 版本和来源 | P0-02/P0-07 前 | 固定版本、构建参数或可信二进制来源、完整依赖和 SHA-256 |
| 可再生成或许可证清晰的 SDR/HDR 测试媒体 | P0-05/P0-10 前 | 本地文件及来源说明；集成测试不依赖易失效公网 URL |
| 本地 HTTP/HLS 测试服务方案 | P0-05 前 | 可重复启动的本地服务和固定测试媒体 |
| HDR 测试硬件与驱动矩阵 | P0-10 前 | 至少一台 HDR 显示器；记录 GPU、驱动、Windows 版本和 HDR 设置 |

如果无法确认原生二进制的架构、依赖闭包、许可证或哈希，不得将其加入正式运行时目录。

## 5. 工作包与依赖

```text
P0-00 基线与输入确认
  └─ P0-01 目标项目骨架和抽象边界
       ├─ P0-02 原生依赖与确定性加载
       │    └─ P0-03 libmpv C ABI 互操作层
       │         └─ P0-04 会话生命周期
       │              └─ P0-05 命令、事件和播放控制 ───────┐
       └─ P0-06 D3D11 与 SwapChainPanel 基线               │
            └─ P0-07 ANGLE/EGL 与 OpenGL FBO ──────────────┤
                                                          ▼
                                              P0-08 Render API SDR 集成
                                                          │
                                              P0-09 覆盖层、输入与生命周期
                                                          │
                                              P0-10 4K、硬解和 HDR 验证
                                                          │
                                              P0-11 切换、清理与 Phase 0 验收
```

P0-02～P0-05 与 P0-06～P0-07 可以在各自前置条件满足后并行实施；P0-08 必须同时等待播放会话和图形链路完成。

## 6. 工作包定义

### P0-00：基线固化与外部输入确认

**目标**

建立可重复的开始状态，消除原生依赖、测试素材和硬件验证方面的未知项。

**工作内容**

- 记录当前提交、SDK、Windows、GPU、驱动和现有构建/测试结果。
- 记录 mpv、ANGLE、测试媒体和本地 HTTP/HLS 服务的来源决策。
- 确认 Phase 0 使用的 x64 开发机和 HDR 验证机。
- 建立原生依赖清单与哈希记录格式，但不提交来源不明的二进制。
- 明确人工验证结果的记录格式：环境、操作、预期、实际、日志、截图或视频证据。

**完成标准**

- `phase-0-progress.md` 中所有 P0-02/P0-03/P0-05/P0-10 前置输入均有明确值或明确阻塞负责人。
- 默认解决方案构建和现有测试结果已记录。
- 没有将来源不明的 DLL 当作实施输入。

**验证**

- `dotnet --info`
- `dotnet build mpv-winui.slnx --no-restore`
- `dotnet test mpv-winui.slnx --no-build --no-restore`

**非目标**

- 不新增项目，不声明 P/Invoke，不播放媒体。

### P0-01：目标项目骨架与抽象边界

**目标**

建立正式目标项目结构，先把旧 HWND/Sidecar 细节从新架构边界中隔离出来，同时保持仓库可构建、可测试。

**预期产物**

- `src/MpvShell.Player.LibMpv/`
- `src/MpvShell.Rendering.WinUI/`
- `tests/MpvShell.Player.LibMpv.Tests/`
- `tests/MpvShell.Rendering.WinUI.Tests/`
- 更新后的 `mpv-winui.slnx`
- 不接收裸 HWND 的播放初始化契约，以及明确的视频表面/渲染生命周期契约

**工作内容**

- 新建目标项目和测试项目，统一 .NET 10、Nullable、x64 与测试依赖配置。
- 为解决方案补齐有效的 `Debug|x64`、`Release|x64` 配置，或采用经过验证且语义等价的 x64 配置方式。
- 调整 `IPlayerBackend`，使播放控制初始化不依赖 HWND。
- 定义应用组合根共享一份 mpv core 的所有权边界；不得让 UI 直接获得原生 mpv handle。
- 为新的接口变更更新 Fake/Recording Backend 测试替身。
- 旧 Sidecar/VideoHost 项目暂不删除，但必须与新项目边界清晰，不允许新项目引用旧项目。

**完成标准**

- 四个目标生产项目和四个目标测试项目均在解决方案中。
- `Player.Abstractions` 不引用 WinUI、D3D11、ANGLE 或 libmpv。
- `IPlayerBackend` 及新控制路径中不存在 `hostHandle`/`HWND` 参数。
- 新项目没有用空成功结果或假状态伪造技术可行性。
- 默认配置和 x64 配置均可构建，现有可保留测试通过。

**验证**

- `dotnet sln mpv-winui.slnx list`
- `dotnet build mpv-winui.slnx -p:Platform=x64`
- `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build`
- `rg -n "hostHandle|HWND|MpvSidecar|VideoHost" src/MpvShell.Player.Abstractions src/MpvShell.Player.LibMpv src/MpvShell.Rendering.WinUI`

**非目标**

- 不加载 DLL，不创建 mpv 会话，不创建 SwapChain，不删除旧项目。

### P0-02：原生依赖清单与确定性加载

**目标**

建立只从固定 `win-x64` RID 目录加载经验证原生依赖的机制，并在进入 ABI 实现前解决依赖闭包和供应链问题。

**预期产物**

- `runtimes/win-x64/native/` 下经过审计的实际依赖闭包。
- 版本、来源、构建参数、许可证和 SHA-256 清单。
- `NativeLibrary.SetDllImportResolver` 驱动的确定性加载器。
- PE 架构、文件缺失、哈希不符和 Client API 主版本不兼容的诊断。

**工作内容**

- 以稳定逻辑库名声明后续 P/Invoke 入口，由 resolver 映射到固定绝对路径。
- 解析路径必须来自应用发布布局，不依赖当前工作目录或 `PATH`。
- 校验进程架构、PE x64、文件哈希和 `mpv_client_api_version` 主版本。
- 审计 `libmpv-2.dll`、`libEGL.dll`、`libGLESv2.dll` 以及构建实际需要的其他 DLL，不假设只有三个文件。
- 错误信息区分缺失、架构错误、哈希错误、依赖加载失败和 API 不兼容。

**完成标准**

- 正确资产可从构建/发布输出固定路径加载。
- 缺失 DLL、错误架构、哈希不符和不兼容 API 均稳定失败并给出可诊断错误。
- 测试不会把开发机全局安装或 PATH 中的 mpv 当作成功条件。
- 清单包含所有随应用分发的原生文件及许可证信息。

**验证**

- 原生清单与实际输出逐文件比对。
- 针对缺失、错误架构、错误哈希的自动化测试。
- x64 发布布局的最小加载烟雾测试。

**非目标**

- 不创建 mpv 会话，不调用渲染 API，不在线下载 DLL。

### P0-03：libmpv C ABI 互操作层

**目标**

依据固定的 mpv v0.41.0 头文件建立最小、可测试、所有权明确的 C# 原生互操作层。

**工作内容**

- 覆盖架构文档第 5.2 节列出的会话、选项、命令、属性、事件、日志和渲染入口。
- 静态入口优先使用 `LibraryImport`；只在源生成器不适用时使用 `DllImport`。
- 明确 `mpv_format`、事件、节点、渲染参数等枚举和结构体的值、布局、对齐和指针语义。
- 明确 UTF-8 输入/输出、`size_t`、非托管字符串、`mpv_free` 和所有回调调用约定。
- 原生回调使用可证明安全的函数指针或明确根定的委托；捕获并隔离托管异常。
- 为 mpv handle 和 render context 设计 `SafeHandle` 或等价明确所有权对象，但不在本包创建真实会话。

**完成标准**

- 所有声明均可追溯到固定版本头文件，不复制来源不明的第三方高层封装。
- ABI 测试覆盖关键枚举值、结构体大小/偏移、UTF-8、空指针、错误字符串和内存释放。
- x64 编译无平台位宽警告；回调声明不依赖 GC 偶然保活。

**验证**

- `dotnet test tests/MpvShell.Player.LibMpv.Tests/MpvShell.Player.LibMpv.Tests.csproj -p:Platform=x64`
- 与固定头文件逐项审查入口签名和结构体布局。

**非目标**

- 不实现播放器状态机，不创建渲染上下文，不设计完整高层封装。

### P0-04：libmpv 会话生命周期

**目标**

在 .NET 10 x64 进程中稳定创建、配置、初始化和销毁一份 libmpv core。

**工作内容**

- 实现 `MpvPlayerSession` 或语义等价的唯一会话所有权对象。
- 在 `mpv_initialize` 前设置禁用 OSC、输入绑定、用户配置、脚本、ytdl 和外部程序调用等确定性选项。
- 设置 Render API 所需的视频输出配置和 Phase 0 选定的初始硬解配置。
- 初始化后启动专用 mpv 事件/命令执行域，但本包只需要最小事件排空和安全关闭。
- 实现幂等、顺序明确的关闭；禁止回调在会话释放后进入托管对象。
- 加入重复创建/销毁和取消初始化的烟雾测试。

**完成标准**

- 真实 `libmpv-2.dll` 上会话创建、初始化、终止和销毁成功。
- 连续至少 100 次创建/销毁无稳定崩溃、挂起或句柄持续增长。
- 初始化失败时已创建资源全部释放，异常保留 mpv 错误信息。
- 会话对象禁止复制所有权，重复 Dispose 安全。

**验证**

- x64 Debug 与 Release 会话烟雾测试。
- 缺失 DLL、API 不兼容、初始化选项错误和取消路径测试。
- 必要时使用进程级句柄/内存观测记录 100 次循环前后数据。

**非目标**

- 不加载媒体，不实现完整属性映射，不创建 render context。

### P0-05：命令、事件、日志与播放控制（Phase 0A）

**目标**

通过 libmpv 异步 API 加载本地 HTTP/HTTPS/HLS 测试媒体，并可靠完成播放、暂停、seek、结束和错误同步。

**工作内容**

- 串行化普通 libmpv 控制调用，持续排空 `mpv_wait_event`。
- 使用结构化参数调用 `mpv_command_async`，为每个请求分配唯一 `reply_userdata`。
- 实现请求完成、错误、超时和调用方取消的关联；取消等待不假定取消 mpv core 内请求。
- 观察 Phase 0A 所需属性，映射到应用强类型状态和事件。
- 将 libmpv 日志接入应用日志，脱敏 URL 查询参数和令牌。
- 使用本地 HTTP/HLS 服务测试，不依赖公网 URL。
- 补全媒体结束、无效 URL、加载失败和会话关闭时的行为。

**完成标准（Gate A）**

- 不启动外部 `mpv.exe`，可加载固定测试媒体。
- 播放、暂停、相对 seek、绝对 seek、结束和加载错误都能通过真实事件同步。
- 并发请求 ID 不串线；事件线程关闭不死锁。
- 日志中不出现完整敏感查询参数。
- Gate A 的自动化和人工证据写入进度文档。

**验证**

- 命令参数、请求关联、超时、取消和事件映射单元测试。
- 本地文件、HTTP 直链、HLS 集成测试。
- 进程列表或测试钩子证明没有启动 `mpv.exe`。

**非目标**

- 不显示视频，不开发轨道面板或完整信息面板。

### P0-06：D3D11 Composition SwapChain 与 SwapChainPanel 基线

**目标**

在不依赖 libmpv 的情况下证明 WinUI 3 可以创建、绑定、resize、Present 和解除 D3D11 Composition SwapChain。

**工作内容**

- 创建 D3D11 设备、DXGI 设备和 Composition SwapChain。
- 通过 `ISwapChainPanelNative.SetSwapChain` 在 UI 线程绑定到 `SwapChainPanel`。
- 用确定颜色或测试图案清屏并 Present，以隔离图形承载问题。
- 将 XAML 逻辑尺寸和 `RasterizationScale` 转换为物理像素，合并连续 resize。
- 实现页面卸载、窗口关闭和设备丢失时的解除与重建顺序。
- 记录是否可用纯 C# 安全表达 COM/资源所有权；只有证据表明确有必要时才提出极薄 C++/WinRT 桥接。

**完成标准**

- 测试图案在窗口、最大化、全屏和多 DPI 下尺寸正确。
- `SetSwapChain` 只在所属 UI 线程调用。
- 先设置空 SwapChain，再释放图形资源。
- 连续 resize 和至少 50 次页面创建/关闭无稳定黑屏、崩溃或资源持续增长。

**验证**

- 尺寸换算和 resize 合并逻辑单元测试。
- 本机人工验证窗口、最大化、全屏、100%/125%/150%/200% DPI。
- 设备重建路径的可控故障注入或最接近的自动化测试。

**非目标**

- 不初始化 ANGLE，不调用 libmpv，不验证 HDR。

### P0-07：ANGLE/EGL、OpenGL 上下文与 FBO

**目标**

在渲染线程上用固定 D3D11 后端初始化 ANGLE/EGL/OpenGL，并把 OpenGL FBO 输出可靠接入 Composition SwapChain。

**工作内容**

- 创建专用渲染线程并由其独占 EGL/OpenGL 上下文。
- 显式选择 ANGLE D3D11 后端，禁止意外回退到其他后端。
- 实现 EGL display、config、surface/context 和 OpenGL FBO 的创建、绑定、resize 与释放。
- 提供后续 `mpv_opengl_init_params.get_proc_address` 使用的函数地址解析器。
- 渲染测试图案，验证 FBO 到 SwapChain 的提交和 Present。
- 定义 UI 线程、渲染线程间的无同步循环等待消息协议。

**完成标准**

- 能证明实际 ANGLE 后端为 D3D11。
- 正确 EGL/OpenGL 上下文只在渲染线程激活。
- FBO 测试图案可稳定显示、resize 和关闭。
- EGL、FBO、SwapChain 和 D3D11 资源释放顺序有自动化覆盖或可审查断言。

**验证**

- ANGLE 后端和 EGL 配置诊断日志。
- 函数地址解析、线程守卫、resize 和重复释放测试。
- 人工验证窗口连续缩放和页面反复进入/退出。

**非目标**

- 不创建 libmpv render context，不验证视频色彩或 HDR。

### P0-08：libmpv Render API 与 SDR 视频（Phase 0B）

**目标**

让 P0-05 的唯一 libmpv core 与 P0-07 的渲染器协作，通过 Render API 在 `SwapChainPanel` 显示色彩正确的 SDR 视频。

**工作内容**

- 控制后端与渲染器共享同一份 `MpvPlayerSession`，但 UI 不接触原生句柄。
- 使用 ANGLE/EGL 解析 `get_proc_address`，创建 libmpv OpenGL render context。
- render update callback 只唤醒渲染线程。
- 渲染线程按顺序调用 `mpv_render_context_update`、绑定 FBO、`mpv_render_context_render`、提交/Present 和 `mpv_render_context_report_swap`。
- 实现新帧、无新帧、resize、暂停、媒体切换和关闭并发场景。
- 记录首帧时间、callback 到 Present 延迟、Present 失败和丢帧指标。

**完成标准（Gate B）**

- SDR 视频通过 Render API 显示在 `SwapChainPanel`，没有外部 mpv 窗口。
- 视频尺寸、宽高比、resize 和 DPI 正确，无持续闪烁或稳定黑屏。
- render context 总是在 mpv core 前释放。
- 连续加载/停止和页面重建无稳定死锁、崩溃或原生资源泄漏。
- SDR 色彩与同版本 mpv 基线无明显错误。

**验证**

- Render API 参数构造、回调寿命、线程守卫和释放顺序测试。
- 固定 SDR 素材的窗口、最大化、全屏、resize 与重复打开关闭人工测试。
- 性能日志包含首帧、Present 和丢帧数据。

**非目标**

- 不宣布 HDR 完成，不开发完整 V1 控件。

### P0-09：XAML 覆盖、输入与完整生命周期（Phase 0C）

**目标**

将现有页面的视频底层替换为正式 `SwapChainPanel`，证明覆盖层、输入和生命周期符合产品要求。

**工作内容**

- 页面层级固定为视频 `SwapChainPanel`、透明交互层、Player Chrome/OSD。
- 将应用 DI 从 Sidecar 切换到共享 `MpvPlayerSession`、libmpv 控制后端和 WinUI 渲染器。
- 保留并适配现有 ViewModel、手势、自动隐藏和错误显示测试。
- 验证点击、拖动、时间轴、键盘和 XAML 动画不会被视频层遮挡或截获。
- 统一页面加载/卸载、窗口关闭、全屏、DPI、显示器切换和设备重建流程。
- 高频播放属性合并后再派发到 UI，避免 UI 持续重绘。

**完成标准（Gate C）**

- XAML 控件稳定覆盖视频并正常响应触摸、鼠标和键盘。
- 视频层不截获 InteractionSurface 输入。
- resize、DPI、全屏和显示器切换无稳定错位、持续闪烁或死锁。
- 现有可保留的 App 测试全部通过，并增加渲染生命周期协调测试。

**验证**

- App 单元测试、生命周期协调测试。
- 鼠标、键盘和真实触屏人工测试。
- 100%/125%/150%/200% DPI，窗口/最大化/全屏和双显示器测试。

**非目标**

- 不完善 V1 视觉设计，不新增媒体库等范围外功能。

### P0-10：4K、硬件解码与 HDR（Phase 0D）

**目标**

用真实硬件和固定素材决定最终 HDR SwapChain 路径，并证明 4K、硬件解码和 HDR 达到硬闸口标准。

**工作内容**

- 比较 10-bit PQ/Rec.2020 与 16-bit float scRGB 两条候选路径。
- 验证 DXGI 格式和 ColorSpace、Advanced Color、ANGLE FBO 精度、`MPV_RENDER_PARAM_DEPTH`、mpv 目标色彩/峰值亮度/tone mapping。
- 验证 4K HEVC Main10、AV1、HDR10、SDR 和 HLS。
- 记录 `hwdec-current`、CPU/GPU/内存、显示/解码丢帧、Present 失败和帧延迟。
- 在 HDR 开/关及 HDR/SDR 显示器切换后重新配置输出。
- 与相同 mpv v0.41.0、相同素材和尽量等价配置比较画质与帧调度。
- 只保留实测通过的一条 HDR 路径；另一条及失败原因写入决策记录。

**完成标准（Gate D）**

- Windows 识别输出为预期 HDR/Advanced Color 路径。
- 10-bit 渐变无明显色带，HDR 高光不错误裁剪，SDR 黑位不抬升。
- HDR 关闭时 tone-map 到 SDR 正确。
- HDR/SDR 显示器切换后输出能正确重配。
- XAML UI 在 HDR 视频上可读且亮度可接受。
- 4K 素材使用预期硬件解码路径，不进行 CPU 逐帧复制再上传。
- 与完整 mpv 对比没有明显画质或帧调度退化。

**验证**

- 至少记录 Windows 版本、GPU、驱动、显示器、HDR 设置、素材、mpv 配置和实际结果。
- Intel/AMD/NVIDIA 未覆盖的路径必须明确标为“未验证”，不能写成支持。
- 通过截图、日志、性能数据和人工观察共同形成证据；截图本身不能证明 HDR 色彩正确。

**停止条件**

- 任一 HDR 核心标准失败时暂停后续产品功能，重新评估渲染实现或上游能力。
- 不得用 CPU 逐帧拷贝、外部 mpv 或降低验收标准绕过失败。

### P0-11：正式切换、遗留清理与 Phase 0 验收

**目标**

在 Gate A～D 全部通过后移除旧路线，使仓库、解决方案、测试和文档只表达正式架构。

**工作内容**

- 删除 `MpvShell.Player.MpvSidecar`、`MpvShell.Interop.VideoHost` 及其旧路线测试。
- 从 App、解决方案、项目引用、命名空间和文档中移除 Sidecar、JSON IPC、`--wid` 和 VideoHost 正式依赖。
- 确认目标四项目和对应四测试项目结构。
- 执行 x64 Debug、Release、发布产物和 DLL 加载烟雾测试。
- 汇总 Phase 0 所有自动化与人工证据，逐条对照 `docs/architecture.md` 第 15 节。
- 更新已验证硬件范围、已知限制、未覆盖矩阵和后续 Phase 1 输入。

**完成标准**

- `mpv-winui.slnx` 不包含旧项目，源码没有旧路线活动引用。
- Gate A、B、C、D 均有可复核证据且状态为通过。
- x64 Debug/Release 构建、全量测试和发布烟雾测试通过。
- `phase-0-progress.md` 的 Phase 0 状态更新为“通过”；如果任一硬闸口未通过，则状态必须保持“阻塞/失败”，不得标记完成。

**验证**

- `dotnet build mpv-winui.slnx -c Debug -p:Platform=x64`
- `dotnet test mpv-winui.slnx -c Debug -p:Platform=x64 --no-build`
- `dotnet build mpv-winui.slnx -c Release -p:Platform=x64`
- Release `win-x64` 发布产物 DLL 加载、会话创建、渲染初始化和销毁烟雾测试。
- `rg -n "MpvSidecar|MpvJsonIpc|MpvProcessManager|VideoHost|--wid|input-ipc-server" src tests mpv-winui.slnx`

## 7. 闸口判定

| 闸口 | 对应工作包 | 通过条件摘要 | 未通过时动作 |
|---|---|---|---|
| Gate A：libmpv 控制 | P0-05 | 真实会话、结构化异步命令、真实事件、无外部 mpv | 停止渲染集成，修复控制和生命周期 |
| Gate B：SDR Render API | P0-08 | Render API 视频进入 SwapChainPanel，SDR/resize/关闭正确 | 停止 UI 扩展和 HDR，修复图形链路 |
| Gate C：覆盖与输入 | P0-09 | XAML 覆盖、触摸/鼠标/键盘、DPI/全屏正确 | 停止完整交互开发，修复视觉树和生命周期 |
| Gate D：4K/HDR | P0-10 | 硬解、GPU 路径、HDR 色彩和多显示器通过 | 暂停产品功能，重新评估渲染或上游能力 |

只有四个闸口全部通过，P0-11 才能把 Phase 0 标记为完成。

## 8. 验证证据要求

每个工作包的进度记录至少包含：

- 提交 SHA 和变更摘要。
- 实际执行的完整命令、退出码和测试数量。
- 自动化未覆盖内容及原因。
- 人工测试环境和步骤。
- 日志、截图、性能数据或转储文件的仓库内路径；敏感 URL 必须脱敏。
- 失败、重试和最终结论，不能只记录最后一次成功。
- 新增技术决策及其依据，尤其是 C++/WinRT 桥接、硬解默认值和 HDR SwapChain 选择。

## 9. 工作包启动提示词模板

```text
阅读 docs/architecture.md、.github/copilot-instructions.md、
docs/implementation/phase-0-plan.md 和
docs/implementation/phase-0-progress.md。

实施工作包 <P0-XX>，不要实施其他工作包。

成功标准：
- 完成该工作包列出的产物和完成标准
- 运行该工作包要求的自动化验证
- 对不能自动验证的项目明确记录人工验证状态
- 更新 phase-0-progress.md，包括命令、结果、证据、阻塞和决策
- 保留用户已有的无关修改，不扩展 Sidecar/--wid 旧路线

如果缺少会改变实现或验收结论的输入，先完成安全的只读审计；仍缺失时停止并报告最小阻塞项，不猜测、不降低验收标准。
```

## 10. Phase 0 之外

本计划不负责 Phase 1～3。Phase 0 通过后，应依据真实实现和硬件验证结果单独编写 Phase 1 实施计划，不应在当前未知项尚未解决时预先展开完整产品计划。
