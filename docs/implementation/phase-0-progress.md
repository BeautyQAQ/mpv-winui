# Phase 0 实施进度

> 总体状态：通过（范围内）。Gate A～D 均有可复核证据；未验证项为用户决定跳过或缺少硬件的双显示器、长时间稳定性、真实触屏、100%/125%/200% DPI，见 `evidence/P0-11-01-phase-0-acceptance.md`
> 当前阶段：Phase 0 已完成（P0-11 于 2026-09-12 收尾）；后续进入 Phase 1 输入整理
> 最后更新：2026-09-12
> 架构基线：`docs/architecture.md` v1.1  
> 执行计划：`docs/implementation/phase-0-plan.md`

2026-09-10：应用已接入新后端；通过原生文件选择器打开用户的 result.mp4 后，SwapChainPanel 显示正向视频，先通过时间轴跳转到 19 秒，后续在 44 秒暂停并保持稳定，媒体信息和单音轨列表均已实测。本机 HTTP 视频播放到 EOF，404 错误后可恢复；F11/Esc 全屏切换、音量/静音和正常关闭均已验证。真实 GPU 测试验证上下色块、64→96→128→64 resize 及同一 mpv core 的渲染上下文重建。Debug/Release 全量构建均为 0 警告、0 错误，各通过 95/95 测试。详细记录见 [SDR MVP 实施记录](mvp-progress.md)；HLS、HDR/硬解及完整 DPI/显示器矩阵仍未验收，MVP 进展不能等同于全部 Phase 0 闸口通过。

## 1. 状态说明

| 状态 | 含义 |
|---|---|
| 未开始 | 尚未实施 |
| 进行中 | 当前正在实施，尚未满足全部完成标准 |
| 阻塞 | 缺少外部输入、权限、硬件或架构决策，无法安全继续 |
| 待人工验证 | 自动化部分完成，但硬件或视觉验收尚未完成 |
| 通过 | 工作包的代码、自动化和要求的人工验证全部通过 |
| 失败 | 已有证据表明硬闸口不满足，需要重新评估 |

只有存在可复核证据时才能标记“通过”。代码合并、能够编译或能够看到画面均不自动等于通过。

## 2. 初始基线与当前回归

当前回归日期：2026-09-12。SDK 为 .NET 10.0.401，Windows x64；旧路线项目已移除，Debug、Release 全量构建均通过且无警告/错误，两种配置各通过 184 个测试、4 项需显式素材的测试按环境跳过（已单独执行）：App 54、LibMpv 79、Rendering 49+4、Abstractions 2。已实际验证 4K HEVC Main10 的 D3D11VA / EGL / P010 GPU 帧传递、AV1 软件回退和 HDR 梯度像素。完整结果与晚间显示器验收见 [HDR/4K 记录](hdr-4k-progress.md)。原 2026-09-10 SDR MVP 发布与 GUI 记录保留在 `artifacts/mvp/`；本轮使用独立的 `artifacts/hdr-4k/`。

以下为 2026-08-28 初始审计快照（P0-00 当日复核），保留历史状态，不代表当前实现：

- 基线提交：`add45b6e9c9634f1b3617013e14ee9e887819c26`（分支 `main`，2026-08-28）
- 解决方案：`mpv-winui.slnx`
- SDK：.NET SDK `10.0.400`（MSBuild 18.9.6，运行时 Microsoft.NETCore.App 10.0.11 / Microsoft.WindowsDesktop.App 10.0.11）
- 开发机（x64 开发机确认）：Windows 11 专业版 10.0.26200，x64，主 GPU NVIDIA GeForce GTX 1060 5GB（驱动 32.0.15.8180，2025-10-29）
- HDR 验证机：未确认（见 EXT-07）
- 目标生产项目：App、Abstractions、LibMpv、Rendering.WinUI 共 4 个；过渡期 Sidecar、VideoHost 曾保留，已于 2026-09-12 P0-11 移除
- 目标测试项目：对应目标生产项目共 4 个；过渡期旧项目测试已随 P0-11 移除
- 默认配置构建：通过，0 警告、0 错误（2026-08-28 P0-01 复核确认）
- 默认配置测试：通过 33 个（2026-08-28 P0-01 复核确认）
  - `MpvShell.Player.Abstractions.Tests`：2
  - `MpvShell.Player.LibMpv.Tests`：2
  - `MpvShell.Rendering.WinUI.Tests`：7
  - `MpvShell.Player.MpvSidecar.Tests`：5
  - `MpvShell.Interop.VideoHost.Tests`：1
  - `MpvShell.App.Tests`：16
- x64 显式配置：通过；解决方案与项目均固定 `Platform=x64`、`PlatformTarget=x64`
- 原生运行时资产：尚未加入仓库
- libmpv 控制、Render API、ANGLE/D3D11、4K 和 HDR：均未实施、未验证

基线命令：

```powershell
dotnet build mpv-winui.slnx --no-restore
dotnet test mpv-winui.slnx --no-build --no-restore
```

P0-01 前的已知失败命令（历史记录，现已修复）：

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
```

历史失败摘要：解决方案配置 `Debug|x64` 无效；P0-01 已增加 x64 配置并验证通过。

## 3. 工作包状态

| 工作包 | 状态 | 负责人/Agent | 开始 | 完成 | 提交 | 备注 |
|---|---|---|---|---|---|---|
| P0-00 基线与输入确认 | 通过 | Copilot Agent | 2026-08-28 | 2026-08-28 | b2f4ccf |
| P0-01 目标项目骨架和抽象边界 | 通过 | Codex | 2026-08-28 | 2026-08-28 | `21070a4` | 目标项目、x64 配置和无 HWND 抽象均已验证 |
| P0-02 原生依赖与确定性加载 | 通过 | Codex | 2026-08-29 | 2026-08-30 | （待提交） | 真实 mpv/ANGLE 构建、四 DLL 闭包、许可证/PE 导入审计、哈希、固定输出加载与 D3D11 EGL 烟雾测试均通过 |
| P0-03 libmpv C ABI 互操作层 | 通过 | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | 已实现固定头文件对应 ABI、UTF-8 和原生参数；MSVC C 布局核验与原生测试通过，待正式工作包验收回填；P0-11 审计回填：ABI 布局/UTF-8/释放顺序测试持续通过 |
| P0-04 会话生命周期 | 通过 | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | 独立事件/命令线程、取消、幂等关闭及 context→core 释放；100 次真实会话循环通过；发布烟雾 3 次会话与外机三轮正常释放 |
| P0-05 命令、事件和播放控制 | 通过 | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | Gate A：本地/HTTP/HLS 控制、暂停、seek、EOF/重播、加载错误恢复、无外部 mpv、日志脱敏均有自动化与人工证据 |
| P0-06 D3D11 与 SwapChainPanel 基线 | 通过 | Copilot Agent / Codex | 2026-08-28 | 2026-08-29 | （待提交） | 清屏/Present、原生面板绑定、窗口尺寸同步及人工硬件验证均通过；Rendering 18 测试通过 |
| P0-07 ANGLE/EGL 与 OpenGL FBO | 通过 | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | D3D11 纹理直接导入 EGL pbuffer；真实 GPU 色块、resize 和上下文重建通过；外机日志证实 ANGLE D3D11 后端与 3840×2160 表面 |
| P0-08 Render API SDR 集成 | 通过 | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | Gate B：Render API 视频进入 SwapChainPanel，150% DPI、全屏、28 次尺寸切换、渲染连续性与释放顺序均有证据 |
| P0-09 覆盖层、输入与生命周期 | 通过（范围内） | Codex | 2026-09-10 | 2026-09-12 | `d7af8a5` | Gate C：鼠标/键盘/全屏/150% DPI/单显示器通过；真实触屏、其他 DPI、双显示器未验证 |
| P0-10 4K、硬解和 HDR 验证 | 通过（范围内） | Codex | 2026-09-11 | 2026-09-12 | `8eb84fa` | RTX 3070 三轮验收：HDR 开关/切换、硬解、PerMonitorV2 下 4K 表面 59.94 fps、EOF 面板保留、正常退出；跨屏与长时间按用户决定不验证；详见 `hdr-4k-progress.md`、`evidence/P0-10-01`、`P0-10-02` |
| P0-11 切换、清理与 Phase 0 验收 | 通过 | Claude | 2026-09-12 | 2026-09-12 | 本次提交 | 旧路线项目移除、Debug/Release 184 通过、发布烟雾与产物启动关闭通过；见 `evidence/P0-11-01-phase-0-acceptance.md` |

## 4. 闸口状态

| 闸口 | 状态 | 自动化证据 | 人工证据 | 结论 |
|---|---|---|---|---|
| Gate A：libmpv 控制 | 通过 | LibMpv 79 测试含真实会话、本地/HTTP/HLS 控制、EOF、取消、错误恢复、无外部 mpv、日志脱敏 | MVP GUI 与 RTX 3070 三轮真实播放、暂停、时间轴 seek、EOF、错误恢复 | 2026-09-12 P0-11 审计通过 |
| Gate B：SDR Render API | 通过 | Rendering 49 测试含真实 GPU 色块方向、resize、同一 core 重建上下文、渲染连续性、10-bit/FP16 精度、应用内 5 场景恢复 | result.mp4 正向显示；RTX 3070 全屏 3840×2160、4K60 59.94 fps、150% DPI 表面等于物理客户区、28 次尺寸切换 | 2026-09-12 P0-11 审计通过；SDR 色彩无仪器对照 |
| Gate C：XAML 覆盖与输入 | 通过（范围内） | App 54 测试含 UI 事件派发、浮层保留、seek 竞争、关闭协调、终态命令拒绝 | 鼠标、键盘、时间轴、双击全屏、F11/Esc、150% DPI（两台机器）、单显示器 | 真实触屏、100%/125%/200% DPI、双显示器未验证 |
| Gate D：4K、硬件解码与 HDR | 通过（范围内） | Debug/Release 各 187 通过、4 项素材驱动测试按环境跳过；真实 HEVC 4K10 硬解、AV1 软件回退、10-bit/FP16 精度、PQ 梯度、渲染连续性通过 | 2026-09-12 RTX 3070 三轮：HDR 开/关与播放中切换、HEVC MP4/TS 硬解、PQ 输出、PerMonitorV2 下 3840×2160 表面 59.94 fps、EOF 面板保留、正常退出；视觉为用户目视；见 `evidence/P0-10-01`、`P0-10-02` | 跨屏与长时间稳定性按用户决定不在本阶段验证；视觉无截图 |

## 5. 外部输入与阻塞项

| 编号 | 输入/阻塞项 | 状态 | 需要时间 | 负责人 | 证据或决定 |
|---|---|---|---|---|---|
| EXT-01 | mpv v0.41.0 x64 libmpv 构建来源、构建参数和许可证 | 已完成 | P0-02 | Agent（执行与审计） | 真实构建 `libmpv-2.dll`；完整静态依赖、LGPL 兼容参数、补丁、系统 DLL 导入和 SHA-256 已登记 |
| EXT-02 | 与二进制匹配的 `client.h`、`render.h`、`render_gl.h` | 已确认并锁定 | P0-03 前 | Agent（执行） | 同 commit `41f6a645…` 三个头文件 SHA-256 已登记在 `build/native/source-lock.json` 与原生依赖清单 |
| EXT-03 | ANGLE x64 固定版本、来源、构建参数和许可证 | 已完成 | P0-02/P0-07 | Agent | 固定 Chrome 152 `chromium/7977`；真实构建三 DLL 闭包并登记哈希；EGL 1.5、OpenGL ES 3.0 与 NVIDIA D3D11 后端烟雾测试通过 |
| EXT-04 | 可再生成或许可证清晰的 SDR 测试媒体 | 已完成当前测试准备 | P0-05 前 | Agent（生成） | 测试代码生成 WAV/Y4M 合成媒体；用户授权本机 result.mp4 用于 GUI 验证，哈希见 MVP 记录；视频及截图不纳入 Git |
| EXT-05 | 本地 HTTP/HLS 测试服务和固定媒体 | HTTP 已完成；HLS 待验证 | P0-05 前 | Agent（实现） | Python 标准库服务仅监听 127.0.0.1 并支持 Range；原生测试使用 .NET 本机 HTTP 服务，已验证控制和 404 恢复；HLS 尚未单独实测 |
| EXT-06 | 4K HEVC Main10、AV1、HDR10 和 10-bit 渐变素材 | 已完成 | P0-10 前 | Codex | 已生成三份固定 4K10-bit 样本并通过哈希、元数据、完整 CPU 解码及无损梯度像素核验，详见 HDR/4K 记录 |
| EXT-07 | HDR 显示器、GPU、驱动和 Windows 测试环境 | 已完成 | P0-10 | 用户 / Codex | 开发机 GTX 1060 5GB；HDR 验证机 RTX 3070（驱动 32.0.16.1692）+ 联合创新 GB27V1 4K 120 Hz（峰值 417 nit，未认证 HDR），Windows HDR 开启，2026-09-12 三轮验收见 `evidence/P0-10-01`、`P0-10-02` |

说明：EXT-01、EXT-02、EXT-03、EXT-04、EXT-05 已有明确来源或实现策略；P0-02 的四个 DLL 全部来自仓库锁定的构建流程，没有来源不明的运行时资产。

2026-09-12 P0-11 完成 Gate A～D 逐项审计：Gate A、B 通过，Gate C、D 在已声明范围内通过。Phase 0 记为「通过（范围内）」，未验证项见 `evidence/P0-11-01-phase-0-acceptance.md`；这些项目不阻塞 Phase 1 开始，但应在 Phase 1 早期补齐。

## 6. 验证记录

每次验证追加记录，不覆盖失败历史。

| 日期 | 工作包 | 环境 | 命令/操作 | 结果 | 证据路径 | 备注 |
|---|---|---|---|---|---|---|
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet build mpv-winui.slnx --no-restore` | 通过：0 警告、0 错误 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 默认配置 |
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet test mpv-winui.slnx --no-build --no-restore` | 通过：21/21 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 默认配置 |
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` | 失败：`Debug|x64` 配置无效 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | P0-01 修复 |
| 2026-08-28 | P0-00 | Windows 11 10.0.26200 x64，GTX 1060 5GB（驱动 32.0.15.8180） | `git log -1` / `dotnet --info` | 通过：基线提交 add45b6，SDK 10.0.400，主机架构 x64 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 环境固化 |
| 2026-08-28 | P0-00 | 同上 | `dotnet build mpv-winui.slnx --no-restore` | 通过：0 警告、0 错误 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 基线复核 |
| 2026-08-28 | P0-00 | 同上 | `dotnet test mpv-winui.slnx --no-build --no-restore` | 通过：21/21（Abstractions 1、MpvSidecar 3、VideoHost 1、App 16） | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 基线复核 |
| 2026-08-28 | P0-00 | 同上 | 全仓扫描 `runtimes/` 目录与 `*.dll/lib/a` 文件（排除 bin/obj） | 通过：仓库中不存在任何原生二进制资产 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 确认无来源不明 DLL |
| 2026-08-28 | P0-01 | .NET SDK 10.0.400，x64 | `dotnet sln mpv-winui.slnx list` | 通过：目标 4 个生产项目与 4 个目标测试项目均存在；旧项目按计划暂留 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 项目骨架 |
| 2026-08-28 | P0-01 | 同上 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` | 通过：12 个项目，0 警告、0 错误 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 显式 x64 |
| 2026-08-28 | P0-01 | 同上 | 新增 Abstractions 架构守卫后的首次 x64 构建 | 失败：测试谓词触发 `CS8122`；改为 LINQ 过滤后重试通过 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 保留失败历史 |
| 2026-08-28 | P0-01 | 同上 | `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore` | 通过：33/33 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 显式 x64 |
| 2026-08-28 | P0-01 | 同上 | 默认配置 build/test | 通过：0 警告、0 错误；33/33 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 默认配置解析为 x64 |
| 2026-08-28 | P0-01 | 同上 | 边界扫描 `hostHandle|HWND|MpvSidecar|VideoHost` | 通过：Abstractions、LibMpv、Rendering.WinUI 无匹配 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 无旧边界泄漏 |
| 2026-08-28 | P0-06 | .NET SDK 10.0.400，x64 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` | 通过：12 个项目，0 警告、0 错误；新增 Vortice.Direct3D11 3.8.3 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | 自动化验证 |
| 2026-08-28 | P0-06 | 同上 | `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build` | 通过：所有测试 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | VideoSurfaceContract：12，ResizeCoalescer：6，总计 18 |
| 2026-08-28 | P0-06 | 同上 | 边界扫描 `hostHandle|MpvSidecar|VideoHost` | 通过：Abstractions、LibMpv、Rendering.WinUI 无匹配 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | 新旧边界隔离 |
| 2026-08-29 | P0-06 | Windows 11 x64，Codex Computer Use | 启动应用并切换窗口/最大化 | 通过：明确蓝色 SwapChain 清屏覆盖完整客户区；Resize 后无白边、黑屏或旧尺寸残留 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | 修复 WinUI 3 原生接口查询与 SizeChanged 转发后复核 |
| 2026-08-29 | P0-06 | 用户测试环境 | 真正全屏、不同 DPI/显示器、连续 50 次生命周期、GPU 内存观察 | 通过 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | 用户确认全部无问题 |
| 2026-08-29 | P0-06 | .NET SDK 10.0.400，x64 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` / `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore` | 通过：构建 0 警告、0 错误；测试 44/44 | `docs/implementation/evidence/P0-06-01-d3d11-swapchain-baseline.md` | 最终回归 |
| 2026-08-29 | P0-02 | .NET SDK 10.0.400，x64 | 固定上游 refs、核对源码头文件/DEPS/LICENSE 哈希 | 通过：mpv `41f6a645…`、ANGLE `736ed80c…` | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 源码供应链锁定 |
| 2026-08-29 | P0-02 | 同上 | `dotnet test tests/MpvShell.Player.LibMpv.Tests/MpvShell.Player.LibMpv.Tests.csproj -p:Platform=x64 --no-restore` | 通过：11/11，0 警告、0 错误 | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 缺失/架构/哈希/API 诊断 |
| 2026-08-29 | P0-02 | 同上 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` / `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore` | 通过：构建 0 警告、0 错误；测试 53/53 | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 全量回归 |
| 2026-08-29 | P0-02 | 本机工具扫描 | `Get-Command` / `vswhere` | 未通过真实构建前置：缺少 C++ Build Tools、depot_tools、GN/Ninja/Meson | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | P0-02 保持进行中 |
| 2026-08-30 | P0-02 | VS 2026、SDK 10.0.26100、Clang/LLD 23、Meson 1.9.2 | `build-angle.ps1` / `build-mpv.ps1` | 通过：ANGLE 396 步；mpv/静态依赖 2660 步；生成四个 x64 DLL | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 兼容问题均以锁定补丁固化 |
| 2026-08-30 | P0-02 | Windows 11 x64、GTX 1060 | 清单比对、`dumpbin`、`test-native-closure.ps1`、`test-angle.ps1` | 通过：无未登记/VC Runtime DLL；mpv API 2.5；ANGLE D3D11 / EGL 1.5 / GLES 3.0 | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 源码资产与应用输出目录均复测 |
| 2026-08-30 | P0-02 | .NET SDK 10.0.400，x64 | `dotnet build` / `dotnet test` | 通过：构建 0 警告、0 错误；测试 53/53 | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 最终回归 |
| 2026-08-30 | P0-02 | .NET SDK 10.0.400，win-x64 | Release 自包含 `dotnet publish` + 两个原生烟雾测试 | 通过：发布目录四 DLL 与清单一致；mpv API 2.5；ANGLE D3D11 | `docs/implementation/evidence/P0-02-01-native-dependency-loading.md` | 发布输出验收 |
| 2026-09-10 | SDR MVP 集成 | .NET SDK 10.0.401，Windows x64 | Debug/Release 全量 build/test | 两种配置均为 0 警告、0 错误，各 95/95 测试通过 | `docs/implementation/mvp-progress.md` | App 34、LibMpv 30、Rendering 23、Abstractions 2、旧项目 6 |
| 2026-09-10 | P0-04/P0-05 | 真实固定 libmpv，x64 | 100 次会话循环、本地/HTTP 控制、EOF 重播、404 后恢复 | 通过 | `tests/MpvShell.Player.LibMpv.Tests/MpvPlayerSessionTests.cs`、`LibMpvHttpTests.cs` | HTTP/Range 已验证，HLS 未单独实测 |
| 2026-09-10 | P0-07/P0-08 | 本机 GPU，ANGLE/D3D11 | 上红下蓝 Y4M，经 64→96→128→64 resize，并在同一 core 重建渲染上下文 | 通过：每轮像素方向及颜色符合预期 | `tests/MpvShell.Rendering.WinUI.Tests/NativeRenderIntegrationTests.cs` | 像素读回仅用于测试；生产渲染无 CPU 逐帧读回 |
| 2026-09-10 | P0-08/P0-09 | Windows x64，GUI | 原生 FilePicker 打开 result.mp4，时间轴跳转19秒，后续44秒暂停，查看详情与轨道 | 通过：正向视频、稳定暂停、H.264/1280×720/29.97 fps/AAC、单音轨 | `artifacts/mvp/evidence/local-playback-info.jpg` | 截图只保留本机，Git 忽略 |
| 2026-09-10 | P0-05/P0-09 | 本机 HTTP 服务，GUI | sample.mp4 播放到 EOF，missing.mp4 返回404，从最近列表重新打开 sample.mp4 | 通过：显示红色错误后可恢复视频并清除错误，最近列表只含两条成功项 | `artifacts/mvp/evidence/http-playback.jpg`、`http-error.jpg` | 截图只保留本机；HLS 未单独实测 |
| 2026-09-10 | P0-08/P0-09 | Windows x64，GUI | F11→Esc、音量/静音、Alt+F4 | 通过：1104×721→1368×912→1104×721；按钮/滑块响应；关闭后进程消失，按 render context→EGL/SwapChain/D3D11→core 释放 | `docs/implementation/mvp-progress.md`；本机会话日志 | 累计呈现7200帧；不等同于完整 DPI/多显示器验收 |
| 2026-09-10 | SDR MVP 发布 | Release win-x64，.NET/WinUI 自包含 | `build/testing/publish-mvp.ps1`，从最终目录运行应用并播放 result.mp4 | 通过：四 DLL 哈希/x64/加载/API2.5、三次会话创建销毁；GUI 正向播放到23秒并暂停 | `artifacts/mvp/win-x64/publish-verification.json`、`artifacts/mvp/evidence/release-playback.jpg` | MVP 已完成；不代表 P0-11 清理或 Gate D 已验收 |
| 2026-09-11 | P0-10 实施与回归 | .NET SDK 10.0.401，Windows x64，GTX 1060 5GB | Debug/Release 全量 build/test，显式提供三份固定 4K 素材 | 两种配置均 0 警告/错误，各 138/138，无跳过 | `artifacts/hdr-4k/evidence/`、`test-results/`、`hardware-reports/`；详情见 `hdr-4k-progress.md` | HEVC Main10 GPU 纹理硬解、AV1 软件回退、10-bit/FP16 精度及 PQ 梯度通过，屏幕验收待进行 |
| 2026-09-11 | P0-10 测试包 | Release win-x64，.NET/WinUI 自包含 | `publish-mvp.ps1 -OutputDirectory artifacts/hdr-4k/win-x64 -NoRestore` | 四 DLL 哈希/x64/加载/API2.5、3 次会话与 35 份许可证原文哈希通过 | `artifacts/hdr-4k/win-x64/publish-verification.json` | 独立目录保留旧 SDR MVP；尚不代表完整 Gate D 通过 |
| 2026-09-12 | P0-10 外机验收 | RTX 3070，Windows 11 26200，GB27V1 4K 120 Hz，Windows HDR 开/关 | 测试包 7f8d613，用户按 8 步顺序验收；日志 MpvShell-RTX3070-Logs-20260912-101312-01da9b11.zip | 部分通过：0 次渲染等待超时、EOF 对齐、HDR 三路径与硬解通过；DPI 不感知致表面最大 2560×1440 | docs/implementation/evidence/P0-10-01-rtx3070-hdr-acceptance.md、rtifacts/rtx3070-analysis/101312-01da9b11/分析报告.md | 已加 PerMonitorV2 清单待复测；跨屏未验证 |
| 2026-09-12 | P0-10 TS 对照 | GTX 1060，本机，同一 LG TS 文件与 17 个目标 | TsSeekDecodePathComparisonTests（MPVSHELL_TEST_TS_MEDIA） | 硬解与软解逐次 seek 错误组完全一致（各 430）；顺播 0 错误 | rtifacts/rtx3070-analysis/101312-01da9b11/local-ts-comparison/ | 定性为 MPEG-TS 随机访问行为，不改解码路径 |
| 2026-09-12 | P0-10 外机复测 | RTX 3070，Windows 11 26200，GB27V1 4K 120 Hz 150% 缩放，Windows HDR 开 | 测试包 8eb84fa，三项复测；日志 MpvShell-RTX3070-Logs-20260912-114448-963fd26d.zip | 通过：全屏 3840×2160 / 缩放 1.0，4K60 HDR TS 59.94 fps、稳态 Render ≤ 10.79 ms，EOF 面板保留，0 错误/警告，退出码 0 | docs/implementation/evidence/P0-10-02-rtx3070-dpi-4k-retest.md、rtifacts/rtx3070-analysis/114448-963fd26d/分析报告.md | 跨屏与长时间按用户决定不验证；视觉为用户目视 |
| 2026-09-12 | P0-05 Gate A | 本机，.NET 10.0.401 | 新增 `HlsMediaServer` 与 `LibMpvHlsTests`（本机播放列表 + ADTS AAC 分片） | 通过：加载/暂停/跨分片 seek/EOF、缺失播放列表报错后会话可用、进程内 libmpv 无外部 mpv.exe；连续 5 次运行稳定 | `tests/MpvShell.Player.LibMpv.Tests/LibMpvHlsTests.cs` | HLS 缺口关闭 |
| 2026-09-12 | P0-11 | 本机，.NET 10.0.401，GTX 1060 150% DPI | 删除旧路线 4 个项目；Debug/Release x64 build/test；`publish-mvp.ps1 -OutputDirectory artifacts/phase-0/win-x64`；发布产物播放 result.mp4 后窗口关闭 | 两种配置 0 警告/错误、各 184 通过 4 跳过；四 DLL 哈希/加载/API 2.5、3 次会话烟雾通过；产物 PerMonitorV2、退出码 0、无 mpv.exe 进程；旧引用扫描仅剩边界测试否定断言 | `docs/implementation/evidence/P0-11-01-phase-0-acceptance.md`、`artifacts/phase-0/win-x64/publish-verification.json` | Phase 0 范围内通过 |
| 2026-09-12 | TS 跳转优化 | 本机 GTX 1060，锁定 libmpv 增量重编 | 新增 mpv demux 补丁与 `demuxer-skip-to-keyframe`；`TsSeekDecodePathComparisonTests` 四变体对照（合成 TS 与 LG TS）；`test-app-recovery.ps1 -Mode paused,playing`；Debug/Release 全量；闭包烟雾 | 产品配置错误组 88→0 / 430→0，落点 ≤ 11 ms，对照变体不变；恢复位置与像素不变；184 通过 4 跳过；闭包与导入不变 | `docs/implementation/evidence/ts-seek-keyframe-2026-09-12.md`、`artifacts/ts-seek/` | 新 libmpv SHA-256 `5E9D2D0D…` |
| 2026-09-12 | TS 跳转优化外机复测 | RTX 3070，GB27V1 4K 120 Hz，Windows HDR 开 | 测试包 `a56cffd`，LG TS 两次加载 24 次精确跳转；日志 `MpvShell-RTX3070-Logs-20260912-134810-6b6e50f0.zip` | 通过：`Could not find ref` 0 条，呈现丢帧 0，落点 ≤ 12 ms，seek 中位数 68 ms，0 错误/警告，退出码 0；用户目视无卡顿 | `artifacts/rtx3070-analysis/134810-6b6e50f0/分析报告.md` | 补丁在目标机生效 |

## 7. 技术决策记录

本表只记录 Phase 0 实施中由实测决定的事项；已经在 `docs/architecture.md` 锁定的路线不在此重新讨论。

| 编号 | 日期 | 决策 | 状态 | 依据 | 影响 |
|---|---|---|---|---|---|
| DEC-01 | 2026-09-11 | HDR 使用 PQ 10-bit / BT.2020 | 已实施；屏幕验收待完成 | P0-10：真实 R10/FP16 存储与 PQ 像素测试，详见 HDR/4K 记录 | 使用 RGB10A2 SwapChain、PQ ColorSpace 与 Render API DEPTH=10；scRGB 的 80 nit 参考白与扩展色域转换未实现 |
| DEC-02 | 2026-09-11 | 优先 D3D11VA + d3d11-egl，失败时软件回退 | 已实施；当前设备实测通过 | GTX 1060 HEVC Main10 硬解与 AV1 软件回退，结果写入解码诊断 | GPU 帧直接传递，禁用 copy-back 路线；其他 GPU 与 AV1 硬解设备待覆盖 |
| DEC-03 |  | 是否增加极薄 C++/WinRT 图形桥接 | 待实测 | P0-06/P0-07 | 项目结构和原生资源所有权；P0-06 已用纯 C# + Vortice 表达 COM/资源所有权，暂不引入 C++ 桥接 |
| DEC-04 |  | 最低 Windows/驱动支持范围 | 待实测 | P0-09/P0-10 | 发布要求和已知限制 |
| DEC-P06-01 | 2026-08-28 | 使用 Vortice.Direct3D11 3.8.3 作为 D3D11/DXGI COM 互操作层 | 已实施 | P0-06 | 成熟的社区库，兼容 .NET 10，免手动 vtable 声明 |
| DEC-P06-02 | 2026-08-28 | ISwapChainPanelNative GUID 为 63aad0b8-7c24-40ff-85a8-640d944cc325 | 已实施 | P0-06 | 来源于 Vortice.WinUI 的 WinUI 3 实现（microsoft.ui.xaml.media.dxinterop） |
| DEC-P06-03 | 2026-08-28 | 物理像素转换使用 Math.Round（默认 MidpointRounding.ToEven） | 已实施 | P0-06 | 与 WinUI 3 和 D3D11 标准对齐 |

## 8. 风险与异常记录

| 编号 | 日期 | 工作包 | 风险/异常 | 影响 | 处理状态 | 结论 |
|---|---|---|---|---|---|---|
| RISK-01 | 2026-08-28 | P0-01 | `.slnx` 缺少有效 `Debug|x64` 配置 | 文档规定的 x64 命令不可用 | 已解决 | P0-01 增加 x64 平台；实际属性、构建和测试均验证通过 |
| RISK-02 | 2026-09-10 | P0-08 | 首轮真实视频上下倒置 | SDR 画面方向错误 | 已解决 | 直接纹理 pbuffer 使用 flipY=false，真实 GPU 色块与用户视频视觉复核均通过 |
| RISK-03 | 2026-09-10 | P0-09 | 初轮 Computer Use GetCursorPos 拒绝访问 | 暂时无法继续 GUI 操作 | 已恢复 | 后续已完成原生文件选择、暂停、seek、详情和轨道验证，不再作为当前阻塞 |

## 9. 人工验证记录格式

所有要求人工验证的工作包（含各 Gate）按以下格式在 `docs/implementation/evidence/` 下建立记录文件（命名：`<工作包>-<序号>-<简述>.md`），并在下方索引表登记。截图/视频/日志原始文件随记录存放于同目录。

每条记录必填字段：

| 字段 | 说明 |
|---|---|
| 日期 / 时间 | 执行验证的本地时间 |
| 工作包 / Gate | 对应工作包编号与闸口 |
| 环境 | 机器标识、Windows 版本、GPU 型号、驱动版本、显示器与 HDR 设置、DPI |
| 操作 | 逐步操作步骤（可复核） |
| 预期 | 来自完成标准的预期结果 |
| 实际 | 实际观察到的结果 |
| 日志 | 应用日志/性能数据文件路径 |
| 截图/视频 | 证据文件路径（截图不能单独作为 HDR 色彩结论） |
| 结论 | 通过 / 失败 / 未验证（失败必须附复现条件） |

记录索引：

| 记录文件 | 工作包 | 日期 | 结论 |
|---|---|---|---|
| `P0-00-01-baseline-verification.md` | P0-00 | 2026-08-28 | 通过 |
| `P0-01-01-project-boundaries.md` | P0-01 | 2026-08-28 | 通过 |
| `P0-02-01-native-dependency-loading.md` | P0-02 | 2026-08-30 | 通过 |
| `P0-06-01-d3d11-swapchain-baseline.md` | P0-06 | 2026-08-29 | 通过 |
| `P0-10-01-rtx3070-hdr-acceptance.md` | P0-10 / Gate D | 2026-09-12 | 部分通过：HDR 开关/切换、硬解、退出通过；DPI 不感知已修待复测，跨屏未验证 |
| `P0-10-02-rtx3070-dpi-4k-retest.md` | P0-10 / Gate D | 2026-09-12 | 通过：PerMonitorV2 生效，4K 表面 59.94 fps，EOF 面板保留；跨屏与长时间按用户决定不验证 |
| `P0-11-01-phase-0-acceptance.md` | P0-11 / Gate A～D | 2026-09-12 | Phase 0 范围内通过：旧路线移除、184 测试、发布烟雾与产物运行；触屏/其他 DPI/双显示器/长时间未验证 |
| `ts-seek-keyframe-2026-09-12.md` | Phase 0 后续优化 | 2026-09-12 | 通过：TS 跳转错误 430 → 0，落点 ≤ 11 ms；RTX 3070 复测 24 次跳转 0 错误、丢帧 0，用户目视无卡顿 |

## 10. 更新规则

每个工作包开始时：

- 将“当前工作包”改为对应编号。
- 将工作包状态改为“进行中”，记录开始日期和执行者。
- 重新检查外部输入与前置工作包。

每个工作包结束时：

- 追加验证命令和实际结果，包括失败记录。
- 填写提交 SHA、完成日期、证据路径和未验证项。
- 只有全部完成标准满足时才标记“通过”。
- 需要真实硬件但尚未测试时标记“待人工验证”，不能标记“通过”。
- 发生阻塞时写明最小缺失输入、已经尝试的安全检查和恢复条件。
- 产生 Phase 0 实测决策时更新技术决策表；不要静默改变架构。

Phase 0 结束时：

- 逐项核对 `docs/architecture.md` 第 15 节。
- Gate A～D 全部通过后，才把总体状态改为“通过”。
- 任一硬闸口失败时，总体状态必须为“失败”或“阻塞”，并暂停 Phase 1 产品功能开发。
