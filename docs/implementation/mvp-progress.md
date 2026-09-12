# SDR 播放 MVP 实施记录

> 当前状态（2026-09-12）：SDR MVP 已完成，后续 Phase 0 已在声明范围内通过；当前工作统一见 [Phase 1 进度](phase-1-progress.md)与[实施计划](phase-1-plan.md)。最新测试包与运行方式见[发布状态](release-status.md)。
> 当前回归：`f793d0b` 的 Debug / Release 构建均为 0 警告、0 错误；Debug 188 通过、无跳过，Release 184 通过、4 项素材测试跳过。证据见 [Phase 1 基线复核](evidence/P1-00-01-baseline-and-release-entry.md)。

本文保留 2026-09-10 的 SDR MVP 实施快照。本地/HTTP 播放、主要 GUI 交互、Debug/Release 回归与自包含发布产物在当日已验证；当时完整 Phase 0、HDR 与硬解验收尚未完成，后续结果见 [Phase 0 进度](phase-0-progress.md)和 [HDR/4K 记录](hdr-4k-progress.md)。下文的测试数量、产物和未验证状态均按日期解读。

## 范围与执行顺序

用户要求继续推进项目，至少完成可用的播放 MVP；提供 `C:\Users\a1426\Downloads\result.mp4` 作为本机测试文件，并明确暂缓 HDR 显示器与验证环境。沿用 libmpv Client/Render API → ANGLE/EGL → D3D11 Composition SwapChain → WinUI 3 SwapChainPanel 的架构；本地文件纳入当前播放范围。

2026-09-10 完成基础 ABI、会话、命令事件、SDR 渲染与应用组合根接入后进行端到端验证。当时 HDR、4K 硬解、多 GPU/显示器矩阵保持未验证，Phase 0 全部闸口尚未验收。

## SDR MVP 实现快照（2026-09-10）

- `MpvPlayerSession` 独立事件/命令线程、唯一请求 ID、超时/取消、幂等关闭。
- `LibMpvBackend` 本地/HTTP/HTTPS 媒体加载，真实暂停、时间、轨道、详情、EOF 与错误事件。
- `MpvRenderContext` 限定渲染线程，持有 core 租约，先于会话销毁。
- ANGLE 与 SwapChain 共享 D3D11 device；直接导入后备纹理作为 EGL pbuffer，生产渲染不使用 CPU 逐帧读回。
- 独立渲染线程处理帧更新、resize、DPI 与资源释放，UI 线程只绑定/解绑面板。
- App 已注册新后端并移除对旧 Sidecar/VideoHost 的活动项目引用；旧项目已于 2026-09-12 P0-11 从仓库与解决方案移除。
- 文件选择/媒体地址、时间轴、播放暂停、音量静音、轨道/信息、全屏、UI 事件调度与关闭协调。

## 测试素材

| 项目 | 值 |
|---|---|
| 文件 | 用户本机 `Downloads\result.mp4` |
| 大小 | 10,595,368 字节 |
| SHA-256 | `c9156cc0dd3ff1e0354ef3960a2c18390eff509d55f6364867b2d5901098830e` |
| 时长 | 54.020633 秒 |
| 视频 | H.264，1280×720，29.970030 fps |
| 音频 | AAC |

## 验证记录

| 检查 | 2026-09-10 证据 |
|---|---|
| 开始前构建/测试 | x64 Debug 构建 0 警告/0 错误，53/53 测试通过 |
| 原生 ABI | 固定三个头文件哈希一致；MSVC 独立 C 编译验证结构布局与关键枚举 |
| 首轮集成构建 | 修正 Vortice MatrixTransform、XAML 控件禁用容器、DispatcherQueueTimer 名称歧义后构建 0 警告/0 错误 |
| 当日完整回归 | Debug 与 Release 全量构建均为 0 警告、0 错误；两种配置各通过 95/95 测试：App 34，LibMpv 30，Rendering 23，Abstractions 2，Sidecar 5，VideoHost 1 |
| 原生依赖 | 四 DLL 哈希/加载成功；Client API 2.5；ANGLE D3D11 / EGL 1.5 / GLES 3.0 |
| 会话与控制 | 真实会话连续创建/销毁 100 次；原生暂停、seek、轨道、EOF 重播、请求关联、取消与错误恢复测试通过 |
| 本地实际播放 | 使用原生 FilePicker 选择用户的 result.mp4，真实视频正向显示；初轮播放日志超过 1200 帧并报告 EOF |
| 时间轴与暂停 | 先通过时间轴跳转到 19 秒，后续播放到 44 秒时暂停，暂停后时间保持不变 |
| 媒体信息与轨道 | GUI 显示 H.264、1280×720、29.97 fps、AAC，轨道面板显示单音轨 |
| 图像方向与 GPU 集成 | 初轮发现上下倒置后将直接纹理 pbuffer 的 flipY 改为 false；正向画面已复核。真实 GPU 像素测试确认上红下蓝，并在 64→96→128→64 resize 及复用同一个 mpv core 重建渲染上下文后保持正确 |
| HTTP 测试准备 | 本地服务 16 个 HTTP/Range/HEAD/404/416 用例通过 |
| HTTP 后端集成 | 真实 libmpv 经本机 HTTP/Range 加载合成媒体，暂停、seek、EOF 与 404 后重新加载测试通过 |
| HTTP 视频 GUI | 本机服务的 sample.mp4 实际播放到 EOF；missing.mp4 返回 404 后出现红色错误提示，最近列表只保留成功打开的两项；点击 sample.mp4 后视频恢复并清除错误 |
| 全屏与窗口恢复 | F11 进入全屏、Esc 恢复窗口；日志尺寸依次为 1104×721→1368×912→1104×721 |
| 音量与静音 | 静音后按钮变为“取消静音”，音量滑块操作后位置变化 |
| 正常关闭 | Alt+F4 后进程消失；日志累计呈现 7200 帧，最后按 render context→EGL/SwapChain/D3D11→mpv 会话顺序释放 |
| 最终发布 | `artifacts/mvp/win-x64/MpvShell.App.exe` 已生成，.NET 与 WinUI 均自包含；EXE 为 304,128 字节，发布报告写入前目录为 273,574,023 字节 |
| 发布原生校验 | 四 DLL 的 SHA-256、x64、加载及 Client API 2.5 校验通过，三次真实 core 初始化/销毁通过；报告位于 `artifacts/mvp/win-x64/publish-verification.json` |
| 发布版 GUI | 从最终发布目录启动后完整显示界面，打开用户 result.mp4，正向播放到 23 秒并成功暂停 |
| UI 自动化 | 初轮发生过 GetCursorPos 拒绝访问；后续已恢复交互并完成上述 GUI 验证，当前不以该历史故障作为阻塞 |

本机 GUI 证据位于 `artifacts/mvp/evidence/`：`local-playback-info.jpg`、`http-playback.jpg`、`http-error.jpg`、`release-playback.jpg`。该目录由 Git 忽略；用户视频及其画面只保留在本机，不纳入提交。

回归命令（两种配置分别执行）：

```powershell
dotnet build mpv-winui.slnx -c Debug -p:Platform=x64 --no-restore
dotnet test mpv-winui.slnx -c Debug -p:Platform=x64 --no-build --no-restore
dotnet build mpv-winui.slnx -c Release -p:Platform=x64 --no-restore
dotnet test mpv-winui.slnx -c Release -p:Platform=x64 --no-build --no-restore
```

历史 SDR MVP 发布与运行命令（2026-09-10，仓库根目录，PowerShell 7）；当前测试包统一见[发布状态](release-status.md)：

```powershell
.\build\testing\publish-mvp.ps1
& .\artifacts\mvp\win-x64\MpvShell.App.exe
```

发布包应整体复制；EXE 依赖同目录内的自包含运行时、WinUI 资源及固定原生 DLL。

## 已知限制与后续状态（更新至 2026-09-12）

- SDR MVP 当日默认软件解码；后续已启用 D3D11VA / d3d11-egl，HDR 与 4K 硬解在已声明硬件范围内通过，详见 [HDR/4K 记录](hdr-4k-progress.md)。
- HLS 已于 2026-09-12 通过本机播放列表集成测试验证（`LibMpvHlsTests`）；自适应码率切换未在范围内。
- “最近打开”保存在当前进程内，重启应用后不保留。
- 真实触屏、100%/125%/200% DPI、Intel/AMD GPU、HDR 仪器测量、HLG/Dolby Vision 等未验证；跨屏和长时间稳定性继续按用户决定延后。已验证 150% DPI 和 RTX 3070 上的 4K 表面，SDR 色块测试仍不能替代完整色彩管理结论。

## MVP 验收结果（2026-09-10 历史快照）

- [x] Debug/Release 全量构建与 95/95 测试。
- [x] 修正后的本地视频方向、暂停与时间轴 seek。
- [x] 原生 EOF 重播、HTTP/Range 控制及 404 后恢复自动化验证。
- [x] 原生文件选择、GUI 轨道与媒体信息。
- [x] 真实 GPU 色块方向、resize 与同一 core 的渲染上下文重建。
- [x] HTTP 视频端到端 GUI 播放到 EOF、404 显示与错误后恢复。
- [x] GUI 音量/静音、全屏切换/窗口尺寸恢复及正常关闭。
- [x] 可运行的 win-x64 发布产物，原生校验与最终目录 GUI 播放通过。
- [x] 回填最终测试数量、已验证范围与剩余限制。
