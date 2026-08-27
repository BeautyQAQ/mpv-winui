# MpvShell 技术方案与实施路线

> 状态：已选定
> 版本：1.1
> 更新日期：2026-08-27

## 1. 决策摘要

MpvShell 是仅面向 Windows 的 WinUI 3 播放器。项目使用 mpv 提供解复用、解码、字幕、音轨和高质量视频渲染能力，由应用自己提供现代化的触屏、键鼠、OSD 和信息界面。

正式技术路线如下：

- 播放内核：`libmpv`
- 控制接口：libmpv C Client API，通过 C# P/Invoke 调用
- 视频输出：libmpv Render API
- 图形桥接：OpenGL ES / ANGLE 到 Direct3D 11
- XAML 承载：WinUI 3 `SwapChainPanel`
- UI：WinUI 3 XAML，覆盖在视频 SwapChain 之上
- 应用基线：.NET 10 LTS（`net10.0-windows10.0.19041.0`）与 Windows App SDK 2.4.0 Stable

以下旧路线正式废弃：

- 不启动外部 `mpv.exe` Sidecar
- 不使用命名管道 JSON IPC 控制播放
- 不使用 `--wid` 子窗口作为最终视频承载方式
- 不维护 Sidecar 与 libmpv 两套正式后端

## 2. 产品目标

### 2.1 核心目标

- 播放 HTTP、HTTPS 视频直链和 HLS/m3u8 流
- 保留 mpv 的格式兼容、硬件解码、字幕和高质量渲染能力
- 支持 4K 视频和 HDR 内容
- 使用 WinUI 3 界面替代 mpv 原生 OSC
- 同时提供自然的触屏、鼠标和键盘操作
- 视频上方可稳定叠加 XAML 控件、动画、面板和错误提示
- 支持窗口、全屏、DPI 缩放和多显示器切换
- 支持详细、易读的媒体与渲染信息面板

### 2.2 V1 功能范围

- URL 输入与播放
- 播放、暂停、停止、相对 seek 和绝对进度跳转
- 音量、静音和倍速
- 时间轴拖拽
- 字幕轨和音轨查询、选择与关闭
- 大尺寸触控控制条与 OSD 菜单
- 详细 HDR/播放信息面板
- 控件自动隐藏
- 横向 seek 与纵向音量手势
- 最近 URL
- 用户可见的加载、播放和渲染错误

### 2.3 暂不包含

- 媒体库、刮削和海报墙
- 商业 DRM
- 浏览器认证和 Cookie 管理界面
- 跨设备同步
- 在线脚本市场
- 多播放内核切换
- 应用运行时在线下载或替换 libmpv

## 3. 选型依据

mpv 官方将 libmpv 定义为“将 mpv 作为其他应用播放后端”时的推荐方式。libmpv 提供 C API，C# 可通过 P/Invoke 直接调用。控制能力仍建立在 mpv 的命令、属性和事件模型之上，但不再需要外部进程、JSON 序列化和命名管道状态同步。

截至 2026-08-27，项目选用当前正式稳定版 `.NET 10` LTS 和 `Windows App SDK 2.4.0`，不使用 Preview 或 Experimental 通道。项目文件必须锁定可重现的具体版本，不使用浮动的“最新版”依赖；.NET 10 和 Windows App SDK 2.x 的后续稳定补丁可通过单独升级提交跟进。

### 3.1 libmpv 与 .NET 10 适配评估

结论：**libmpv 与 .NET 10 在架构上适配，没有已知的 .NET 版本级阻塞。**

libmpv 暴露的是稳定 C ABI，不链接 CLR，也不依赖 .NET 的托管 ABI 或目标框架。.NET 10 继续支持 P/Invoke、`LibraryImport`、非托管函数指针、`UnmanagedCallersOnly` 和 `SafeHandle`，因此不需要专门的“.NET 10 版 libmpv”。同一份符合目标架构的 `libmpv-2.dll` 可以由 .NET 8、9 或 10 应用调用。Windows App SDK 与 libmpv 也没有直接版本耦合，两者只在本项目的渲染层交汇。

需要验证的是托管/原生边界，而不是 libmpv 对 .NET 10 的源码级适配：

- P/Invoke 声明的参数、返回值、结构体布局和枚举值必须与固定版本的 mpv 头文件一致。
- UTF-8 字符串、`size_t`、指针宽度和原生内存释放必须显式处理。
- x64 仍然是 V1 唯一目标；不允许混用 x86、x64 或 ARM64 DLL。
- 原生回调不得让托管异常跨越 ABI 边界，且必须在注销回调前保证函数指针或委托的生命期。
- 应用启动时必须检查 `mpv_client_api_version` 的主版本，CI 必须针对 .NET 10 x64 发布产物执行实际 DLL 加载和会话创建烟雾测试。

mpv 对 libmpv C API 的承诺是：不兼容变更只能在 Client API 主版本升级时发生，ABI 保证向后兼容。项目仍然固定 mpv `v0.41.0` 及其实际构建产物，不在运行时随意替换 DLL。

### 3.2 Render API 选择

libmpv 有两种视频嵌入方式：

1. 将 mpv 原生窗口附着到 `wid`。
2. 使用 Render API 将画面渲染到调用方管理的图形表面。

本项目需要在视频上放置大量 XAML 控件并处理触摸输入，因此选择 Render API。`SwapChainPanel` 能把 DirectX SwapChain 放入 XAML 视觉树，并允许其他 XAML 元素覆盖在视频上。

该路线增加了图形互操作和线程管理复杂度，但它直接解决了旧 `--wid` 方案最关键的窗口层级、覆盖层和输入问题。

## 4. 总体架构

```text
┌─────────────────────────────────────────────────────────┐
│                     MpvShell.App                         │
│  WinUI 页面 / ViewModel / 输入 / OSD / 面板 / 导航      │
└──────────────────────────┬──────────────────────────────┘
                           │
             ┌─────────────┴─────────────┐
             │                           │
             ▼                           ▼
┌────────────────────────┐  ┌─────────────────────────────┐
│ Player.Abstractions    │  │ Rendering.WinUI             │
│ 命令、状态、事件模型    │  │ SwapChainPanel / D3D11      │
│ IPlayerBackend         │  │ ANGLE / EGL / Render Thread │
└────────────┬───────────┘  └──────────────┬──────────────┘
             │                              │
             └──────────────┬───────────────┘
                            ▼
                 ┌──────────────────────┐
                 │ Player.LibMpv        │
                 │ libmpv 会话与事件循环 │
                 │ C API P/Invoke       │
                 └──────────┬───────────┘
                            ▼
                 ┌──────────────────────┐
                 │ libmpv-2.dll         │
                 │ 解复用/解码/字幕/渲染 │
                 └──────────────────────┘
```

### 4.1 建议项目结构

```text
src/
  MpvShell.App/
  MpvShell.Player.Abstractions/
  MpvShell.Player.LibMpv/
  MpvShell.Rendering.WinUI/

tests/
  MpvShell.Player.Abstractions.Tests/
  MpvShell.Player.LibMpv.Tests/
  MpvShell.Rendering.WinUI.Tests/
  MpvShell.App.Tests/
```

职责如下：

- `MpvShell.App`
  - WinUI 3 页面和应用生命周期
  - ViewModel 与交互协调
  - OSD、轨道面板、信息面板和错误提示
- `MpvShell.Player.Abstractions`
  - 与 mpv 无关的命令、状态和事件模型
  - `IPlayerBackend`
  - 不引用 WinUI、ANGLE、D3D11 或 libmpv
- `MpvShell.Player.LibMpv`
  - libmpv 动态加载和 API 版本检查
  - mpv 句柄、命令、属性、事件和日志
  - 事件线程与异步请求关联
  - mpv 原始数据到应用模型的映射
- `MpvShell.Rendering.WinUI`
  - `SwapChainPanel` 原生接口
  - D3D11 设备与 DXGI SwapChain
  - ANGLE/EGL/OpenGL 上下文
  - libmpv Render API 上下文
  - 渲染线程、尺寸变化、HDR 和设备丢失恢复

渲染互操作优先使用 C# 与受控的 `unsafe` 原生互操作。.NET 10 下的静态入口优先使用源生成的 `LibraryImport`，只在源生成器不适用时使用 `DllImport`；原生资源用 `SafeHandle` 或明确所有权对象管理。只有在 COM、EGL 或资源所有权无法可靠表达时，才增加一个很薄的 C++/WinRT 桥接项目；上层架构不得依赖具体桥接语言。

## 5. 播放后端设计

### 5.1 抽象边界

`IPlayerBackend` 不再接收裸 `HWND`。`HWND` 是旧 `wid` 方案的实现细节，不属于播放器控制抽象。

后端抽象至少包含：

- 初始化和关闭
- 加载 URL
- 播放、暂停和停止
- 相对和绝对 seek
- 音量、静音和倍速
- 字幕轨与音轨
- 属性查询
- 状态与错误事件

视频表面由独立渲染接口管理。应用组合根创建一份 `MpvPlayerSession`；控制后端和渲染器共享同一个 mpv core，但 UI 不直接接触原生句柄。

### 5.2 C# 原生调用层

项目自己维护最小原生互操作层，不以第三方高层封装作为架构基础。固定入口优先以 `LibraryImport` 生成 P/Invoke 封送；原生回调在签名允许时优先使用 `UnmanagedCallersOnly` 和非托管函数指针，否则使用明确根定的委托。首批覆盖以下稳定 API：

`LibraryImport` 使用稳定的逻辑库名，应用通过 `NativeLibrary.SetDllImportResolver` 将它解析到 RID 目录下经哈希校验的固定 DLL；不修改进程级 DLL 搜索路径，不从当前工作目录或 `PATH` 隐式加载 libmpv。

- 会话：`mpv_client_api_version`、`mpv_create`、`mpv_initialize`、`mpv_terminate_destroy`
- 选项：`mpv_set_option`、`mpv_set_option_string`
- 命令：`mpv_command_async`
- 属性：`mpv_get_property_async`、`mpv_set_property_async`、`mpv_observe_property`
- 事件：`mpv_wait_event`、`mpv_set_wakeup_callback`
- 日志与错误：`mpv_request_log_messages`、`mpv_error_string`、`mpv_free`
- 渲染：`mpv_render_context_create`、`mpv_render_context_set_update_callback`、`mpv_render_context_update`、`mpv_render_context_render`、`mpv_render_context_report_swap`、`mpv_render_context_free`

原生资源必须由 `SafeHandle` 或明确的所有权对象管理。UTF-8 字符串、结构体布局、回调委托寿命和非托管内存释放都需要测试覆盖。

### 5.3 初始化配置

在 `mpv_initialize` 之前设置确定性选项：

- 禁用 mpv 原生 OSC
- 禁用默认鼠标和键盘绑定
- 禁用用户级配置和自动脚本加载
- 默认禁用 ytdl，V1 只接收直接媒体 URL
- 视频输出设为 libmpv Render API
- 硬件解码使用通过 Phase 0 验证的配置
- 日志通过 libmpv 日志事件进入应用日志系统

应用不能隐式读取用户电脑上已有的 `mpv.conf` 或脚本目录，否则行为和测试结果不可重复。

### 5.4 命令与事件

- 使用结构化参数的 `mpv_command_async`，不拼接命令字符串。
- UI 高频输入先节流；时间轴拖动期间不能为每个像素发送 seek。
- 每个异步请求分配唯一 `reply_userdata`，关联完成、错误和超时。
- C# `CancellationToken` 只能取消调用方等待；已经进入 mpv core 的请求仍按 libmpv 语义处理完成事件。
- UI 不读取 mpv 原生结构，后端负责转换为稳定的应用模型。

首批观察属性包括：

- `pause`、`time-pos`、`duration`、`seeking`
- `volume`、`mute`、`speed`
- `paused-for-cache`、`cache-buffering-state`
- `track-list`、`vid`、`aid`、`sid`
- `video-params`、`audio-params`
- `video-format`、`video-codec`、`audio-codec-name`
- `hwdec-current`、`estimated-vf-fps`、`container-fps`
- `vo-drop-frame-count`、`decoder-frame-drop-count`

事件层输出应用自己的强类型事件，例如 `PlaybackStateChanged`、`TracksChanged`、`MediaInfoChanged`、`BufferingChanged`、`EndReached`、`PlayerFaulted` 和 `RendererFaulted`。

## 6. 视频渲染方案

### 6.1 渲染链路

```text
解码帧
  │
  ▼
libmpv Render API
  │ OpenGL FBO
  ▼
ANGLE / EGL
  │ D3D11 资源
  ▼
DXGI Composition SwapChain
  │
  ▼
WinUI 3 SwapChainPanel
  │
  ├─ XAML 触控层
  ├─ 控制条与 OSD
  ├─ 字幕/音轨面板
  └─ 信息与错误面板
```

目标是在支持的硬件解码路径上保持 GPU 内部传递，禁止将 4K 视频逐帧复制到 CPU 内存后再上传。

### 6.2 SwapChainPanel

- 使用 `ISwapChainPanelNative.SetSwapChain` 绑定 Composition SwapChain。
- `SetSwapChain` 必须在所属 UI 线程调用。
- 视频面板位于 XAML 层级底部。
- 透明交互层位于视频上方，统一接收触摸和鼠标事件。
- OSD、控制条和面板是普通 XAML 元素。
- 释放页面或重建设备时，先向面板设置空 SwapChain，再释放图形资源。

建议页面层级：

```xml
<Grid>
    <SwapChainPanel x:Name="VideoSurface" />
    <Grid x:Name="InteractionSurface" Background="Transparent" />
    <Grid x:Name="PlayerChrome" />
</Grid>
```

### 6.3 ANGLE、帧调度与尺寸

- ANGLE 固定使用 Direct3D 11 后端。
- EGL 上下文和 OpenGL FBO 由渲染线程独占。
- `mpv_opengl_init_params.get_proc_address` 从 ANGLE/EGL 解析函数地址。
- 调用 `mpv_render_*` 前，正确的 EGL/OpenGL 上下文必须在当前线程激活。
- 不使用已废弃的 `opengl-cb` API。

帧调度流程：

1. render update callback 只发送轻量唤醒信号。
2. 渲染线程调用 `mpv_render_context_update`。
3. 有新帧时绑定 FBO 并调用 `mpv_render_context_render`。
4. 提交图形资源并调用 SwapChain `Present`。
5. 调用 `mpv_render_context_report_swap` 反馈实际呈现。

XAML 尺寸为逻辑像素，SwapChain 使用物理像素。物理尺寸由 `RasterizationScale` 计算；窗口连续调整时合并 resize 请求。全屏、恢复窗口、DPI 和显示器变化使用同一尺寸更新流程。

## 7. 线程模型

项目固定使用三个逻辑执行域：

### UI 线程

- WinUI 元素与 `SwapChainPanel` 绑定
- ViewModel 状态更新
- 指针、触摸、键盘和命令入口
- 把输入写入播放器命令队列

### mpv 事件/命令线程

- 串行处理 libmpv 控制调用
- 持续排空 `mpv_wait_event`
- 解析属性、请求回复、日志和错误
- 生成不可变的应用事件快照
- 通过 `DispatcherQueue` 或线程安全通道通知 UI

### 渲染线程

- 独占 EGL/OpenGL 上下文
- 调用 `mpv_render_*`
- 管理 D3D11、FBO、SwapChain 和 Present
- 不调用普通 libmpv 控制 API
- 不等待 UI 线程持有的锁

任何线程之间都不得形成同步循环等待。事件和渲染回调只负责唤醒，实际工作由对应线程完成。

## 8. 生命周期与恢复

### 8.1 启动顺序

1. 按进程架构加载固定路径下的 `libmpv-2.dll`。
2. 检查 libmpv Client API 主版本兼容性。
3. 创建 mpv handle，设置选项并初始化 mpv core。
4. 创建 D3D11、ANGLE/EGL 和 SwapChain。
5. 创建 libmpv render context。
6. 注册事件唤醒和渲染更新回调。
7. 把 SwapChain 绑定到 `SwapChainPanel`。
8. 启动事件线程和渲染线程。
9. 允许加载媒体。

### 8.2 关闭顺序

1. UI 停止接受新命令。
2. 取消事件、渲染和 resize 调度。
3. 从 `SwapChainPanel` 解除 SwapChain。
4. 释放 libmpv render context。
5. 释放 FBO、EGL、ANGLE、SwapChain 和 D3D11 资源。
6. 终止并销毁 mpv handle。
7. 卸载原生库。

render context 必须先于 mpv core 释放。回调委托的托管引用在原生回调注销前不能被 GC。

### 8.3 错误恢复

- URL 错误只终止当前加载，不销毁播放器会话。
- Render context 错误先尝试重建渲染链路。
- D3D 设备丢失时重建 D3D11、ANGLE 和 SwapChain。
- 原生库加载失败、API 不兼容或连续恢复失败时进入不可恢复错误页。
- libmpv 位于应用进程内；原生崩溃会终止整个应用，因此需要保存诊断日志并接入 Windows 崩溃转储策略。

## 9. HDR 与色彩管理

HDR 是硬性技术目标。“能播放 HDR 文件并看到画面”不等于 HDR 支持完成。

### 9.1 验证路径

先完成色彩正确的 SDR Composition SwapChain，再在 Phase 0 中比较：

- 10-bit PQ / Rec.2020 SwapChain
- 16-bit float scRGB SwapChain

最终只保留实测通过的一种。验证内容包括：

- DXGI SwapChain 格式和 ColorSpace
- 当前显示器 Advanced Color 状态
- ANGLE FBO 格式和精度
- `MPV_RENDER_PARAM_DEPTH`
- mpv 目标色彩、峰值亮度与 tone mapping 配置
- XAML 合成对 HDR SwapChain 的处理

### 9.2 HDR 通过标准

- Windows 能识别输出为 HDR/Advanced Color 路径。
- 10-bit 渐变无明显色带。
- HDR 高光不被错误裁剪，SDR 黑位不抬升。
- HDR 开关关闭时正确 tone-map 到 SDR。
- 窗口在 HDR 与 SDR 显示器间移动后能重新配置。
- 视频上方 XAML UI 保持可读，白色控件亮度可接受。
- 对比相同配置的 mpv 0.41 稳定版，画质和帧调度没有明显退化。

正式 libmpv Render API 与完整 mpv 播放器当前默认的 `gpu-next` 路径并不完全等价。Phase 0 必须以实际 HDR 素材验证，不能根据配置名称推断结果。HDR 闸口不通过时暂停产品功能开发，先重新评估渲染实现或上游能力。

## 10. 原生依赖与许可证

### 10.1 版本和架构

- 首个技术基线固定为 mpv `v0.41.0`。
- 不自动跟随每日构建。
- 每次升级单独提交并记录 API、构建配置、依赖版本和验证结果。
- Phase 0 与首个 V1 只要求 `win-x64`。
- `win-arm64` 在 x64 稳定后单独立项；不支持 x86。

原生文件按 RID 放置：

```text
runtimes/
  win-x64/native/
    libmpv-2.dll
    libEGL.dll
    libGLESv2.dll
```

具体清单以构建产物审计为准，不能假设只有上述三个 DLL。

### 10.2 供应链和许可证

- 正式发布优先使用项目自己的可重复构建流程。
- 固定 mpv、FFmpeg、libplacebo、ANGLE 和其他原生依赖版本。
- 保存构建参数、补丁和 SHA-256。
- CI 校验 DLL 哈希与 PE 目标架构。
- 应用不从网络动态下载原生库。
- libmpv 以 LGPL 兼容构建为目标；`-Dgpl=false` 只是起点。
- 同时审计 FFmpeg、libplacebo、ANGLE 和其他链接依赖的实际许可证。
- 发布包包含许可证、版权声明、第三方通知和对应源代码信息。

## 11. 输入与安全边界

- V1 只接受 `http` 和 `https` URL；m3u8 是媒体格式，不是新协议。
- URL 不进入 shell 或命令行，只通过结构化 libmpv 命令传递。
- 默认不加载用户级 mpv 配置、Lua/JavaScript 脚本或插件。
- 默认关闭 ytdl 和任意外部程序调用。
- 日志对 URL 查询参数和潜在令牌脱敏。
- 对连续失败、超长 URL 和重复加载做合理限制。

## 12. 应用状态模型

应用维护自己的统一状态，不让 ViewModel 直接依赖 mpv 属性名称。

- 播放状态：空闲、打开中、缓冲、播放、暂停、结束和错误
- 时间状态：当前位置、总时长和 seek 状态
- 控制状态：倍速、音量和静音
- 轨道状态：视频、音频、字幕列表及当前选择
- 渲染状态：SDR/HDR、解码器、硬件解码、尺寸、帧率、位深、色彩空间和丢帧
- UI 状态：控件可见性、当前浮层、全屏、手势和错误恢复

高频属性应合并后再进入 UI，避免进度、缓存和帧统计使 UI 线程持续重绘。

## 13. 测试策略

### 13.1 自动化测试

- P/Invoke 枚举值和结构体布局
- `LibraryImport` 入口、回调签名和调用约定
- UTF-8 编组和原生内存释放
- libmpv 错误码转换
- 请求 ID 与异步回复关联
- 属性/事件到应用状态的映射
- 轨道列表和 HDR 元数据解析
- 手势、命令节流和自动隐藏
- 生命周期状态机和重复释放安全性
- 本地媒体、HTTP 直链和 HLS 集成测试
- libmpv DLL 缺失、架构错误和版本不兼容
- .NET 10 x64 发布产物的 DLL 加载、会话创建与销毁烟雾测试
- Render API 初始化、resize 和设备重建

测试媒体必须可再生成或许可证清晰，不能依赖可能失效的公网 URL。

### 13.2 人工硬件矩阵

- Windows 11，HDR 开/关
- Windows 10 SDR 基线
- Intel、AMD、NVIDIA 至少各一种硬件路径
- 单显示器和 HDR/SDR 双显示器
- 100%、125%、150%、200% DPI
- 窗口、最大化和全屏
- 鼠标键盘和触屏设备
- H.264、HEVC Main10、AV1、HDR10 和 HLS

### 13.3 性能观测

记录首帧时间、解码方式、CPU/GPU/内存、解码与显示丢帧、Present 失败、render callback 到 Present 延迟，以及 resize 和显示器切换耗时。

## 14. 分阶段实施计划

### Phase 0：技术可行性闸口

目标：证明选定架构能满足最难的渲染、覆盖层和 HDR 要求。

#### 0A：libmpv 控制原型

- 将原型目标框架固定为 `.NET 10` x64
- 加载固定版本 DLL 并检查 API
- 创建和销毁 mpv 会话
- 加载 URL
- 实现事件循环和日志
- 验证播放、暂停、seek 和结束事件

#### 0B：SwapChainPanel SDR 原型

- 创建 D3D11 Composition SwapChain
- 初始化 ANGLE/EGL/OpenGL
- 创建 libmpv render context
- 在 SwapChainPanel 显示 SDR 视频
- 正确处理 resize、DPI、全屏和关闭

#### 0C：XAML 覆盖和输入

- 视频上显示可点击 XAML 控件
- 验证触摸、鼠标和键盘
- 验证动画和自动隐藏
- 确认视频层不会遮挡或截获上层输入

#### 0D：4K HDR 与硬件解码

- 选择并验证最终 HDR SwapChain 路径
- 验证 4K HEVC Main10 和 AV1
- 验证硬件解码和 GPU 资源传递
- 完成 HDR/SDR 与多显示器切换测试
- 与同版本 mpv 对比画质和帧调度

Phase 0 全部通过前，不继续堆叠完整产品 UI。

### Phase 1：生产级播放基础

- 固化模块结构和资源所有权
- 完成后端抽象与应用状态模型
- 完成命令队列、事件映射和错误分类
- 完成轨道、媒体信息、缓存和日志
- 完成设备丢失、页面重建和应用退出
- 建立原生依赖打包、哈希和许可证流程

### Phase 2：V1 播放器交互

- URL 输入和最近 URL
- 大尺寸底部控制条
- 时间轴拖拽与 seek 预览
- 字幕和音轨面板
- OSD 与详细信息面板
- 触屏手势和自动隐藏
- 窗口与全屏体验

### Phase 3：稳定性与发布

- 全量自动化测试
- 人工硬件矩阵
- 长时间播放和反复打开关闭
- 崩溃日志与设备恢复验证
- x64 发布包
- 第三方许可证和源代码通知
- 性能基线与已知限制文档

## 15. Phase 0 通过标准

只有同时满足以下条件，才确认技术选型成功：

- 不启动外部 `mpv.exe`。
- C# 能稳定创建、控制和销毁 libmpv 会话。
- 视频通过 Render API 显示在 `SwapChainPanel`。
- XAML 控件能覆盖视频并正常接收触摸和鼠标。
- 窗口、全屏、DPI 和 resize 无黑屏、错位或持续闪烁。
- 4K 流使用预期硬件解码路径。
- SDR 色彩正确。
- HDR 输出通过第 9 节验收，而非仅能显示画面。
- 播放、暂停、seek、缓冲、结束和错误能可靠同步到 UI。
- 连续创建/销毁和设备重建无稳定复现的崩溃或原生资源泄漏。

任何一项失败都不能用 UI 假数据、CPU 逐帧拷贝或重新启动外部 mpv 进程绕过。

## 16. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|---|---|---|
| ANGLE/OpenGL/D3D11 互操作复杂 | 黑屏、拷贝、设备丢失 | Phase 0 独立原型；渲染代码与 UI 分层 |
| Render API 与完整 mpv 默认渲染路径不同 | HDR 或画质退化 | 固定版本、同素材对比、HDR 硬闸口 |
| 渲染与事件线程死锁 | 卡顿或冻结 | 严格三线程模型；回调只唤醒；禁止循环等待 |
| libmpv 在进程内崩溃 | 整个应用退出 | 固定依赖、输入边界、崩溃转储、升级回归 |
| 原生 DLL 来源或许可证不清晰 | 供应链和发布风险 | 自建、锁版本、哈希、许可证审计 |
| HDR 显示器和驱动差异 | 不同机器结果不一致 | Intel/AMD/NVIDIA 与多显示器矩阵 |
| 高频属性压垮 UI | 卡顿和耗电 | 后端合并状态，按 UI 可见频率派发 |
| 过早开发完整 UI | 技术失败后返工 | Phase 0 通过前只做验证 UI |

## 17. 决策状态

### 已锁定

- 使用 .NET 10 LTS 和 Windows App SDK 2.4.0 Stable，不使用 Preview/Experimental 作为产品基线。
- 使用 libmpv，不使用外部 mpv Sidecar。
- 使用 Render API，不使用 `--wid` 作为最终方案。
- 使用 WinUI 3 `SwapChainPanel`。
- 使用 ANGLE 将 OpenGL Render API 接入 D3D11。
- UI 与视频在同一 XAML 视觉树中合成。
- x64 优先。
- Phase 0 先验证渲染和 HDR，再开发完整产品功能。

### 由 Phase 0 实测决定

- HDR 最终采用 PQ 10-bit 还是 scRGB 16-bit float SwapChain。
- 硬件解码默认配置。
- 是否需要极薄的 C++/WinRT 图形桥接层。
- 最终支持的最低 Windows 版本和驱动范围。
- 是否以及何时增加 ARM64。

## 18. 参考资料

- [.NET 官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Windows App SDK 最新稳定版下载](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
- [.NET 原生互操作最佳实践](https://learn.microsoft.com/dotnet/standard/native-interop/best-practices)
- [mpv：Embedding into other programs (libmpv)](https://github.com/mpv-player/mpv/blob/master/DOCS/man/libmpv.rst)
- [mpv：CLI 与 API 兼容性策略](https://github.com/mpv-player/mpv/blob/master/DOCS/compatibility.rst)
- [mpv Client API](https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)
- [mpv Render API](https://github.com/mpv-player/mpv/blob/master/include/mpv/render.h)
- [mpv OpenGL Render API](https://github.com/mpv-player/mpv/blob/master/include/mpv/render_gl.h)
- [mpv 官方 libmpv 示例](https://github.com/mpv-player/mpv-examples/tree/master/libmpv)
- [mpv Windows 构建文档](https://github.com/mpv-player/mpv/blob/master/DOCS/compile-windows.md)
- [WinUI 3 ISwapChainPanelNative](https://learn.microsoft.com/windows/windows-app-sdk/api/win32/microsoft.ui.xaml.media.dxinterop/nn-microsoft-ui-xaml-media-dxinterop-iswapchainpanelnative)
- [DirectX 与 XAML 互操作](https://learn.microsoft.com/windows/uwp/gaming/directx-and-xaml-interop)
- [Richasy/mpv-winui](https://github.com/Richasy/mpv-winui)
