# P0-11 正式切换、遗留清理与 Phase 0 验收（2026-09-12）

| 字段 | 内容 |
|---|---|
| 日期 / 时间 | 2026-09-12 12:00 至 12:30（北京时间） |
| 工作包 / Gate | P0-11；Gate A～D 汇总审计 |
| 环境 | 开发机 Windows 11 10.0.26200，GTX 1060 5GB（驱动 32.0.15.8180），2560×1440 @ 150%；外机 RTX 3070（驱动 32.0.16.1692），GB27V1 3840×2160 @ 120 Hz 150%，Windows HDR 开/关；.NET SDK 10.0.401 |
| 操作 | 删除 `MpvShell.Player.MpvSidecar`、`MpvShell.Interop.VideoHost` 及其测试并从解决方案移除；补齐 Gate A 的 HLS 集成测试；x64 Debug/Release 全量构建与测试；Release 自包含发布并运行 DLL 加载、3 次会话创建/销毁烟雾测试；从发布目录启动应用播放本地 MP4 后经窗口关闭退出；对照 `docs/architecture.md` 第 15 节逐项核对 |
| 结论 | **Phase 0 在已声明范围内通过。** 未验证项见文末，均为用户决定跳过或缺少硬件的项目，不是失败项 |

## 清理结果

- 解决方案只含目标 4 个生产项目与 4 个测试项目。`git rm` 移除 19 个旧路线文件。
- 扫描 `rg "MpvSidecar|MpvJsonIpc|MpvProcessManager|VideoHost|--wid|input-ipc-server" src tests mpv-winui.slnx`：仅剩两处边界测试的否定断言（LibMpv 与 Rendering 程序集不得引用旧项目），无活动引用。
- `.github/copilot-instructions.md` 改为「旧路线已移除，不得重新引入」。`publish-mvp.ps1` 仍保留对 `mpv.exe`、旧 DLL 的发布目录否定检查。

## 构建、测试与发布

| 项 | 结果 |
|---|---|
| `dotnet build mpv-winui.slnx -c Debug -p:Platform=x64` | 0 警告、0 错误 |
| `dotnet test mpv-winui.slnx -c Debug -p:Platform=x64 --no-build` | 184 通过、0 失败、4 跳过（App 54、LibMpv 79、Rendering 49+4、Abstractions 2） |
| `dotnet build mpv-winui.slnx -c Release -p:Platform=x64` | 0 警告、0 错误 |
| `dotnet test mpv-winui.slnx -c Release -p:Platform=x64 --no-build` | 184 通过、0 失败、4 跳过 |
| `publish-mvp.ps1 -OutputDirectory artifacts/phase-0/win-x64` | 四原生 DLL 哈希/x64/加载、Client API 2.5、3 次会话创建销毁通过；发布目录无 `mpv.exe`、旧 DLL；报告 `artifacts/phase-0/win-x64/publish-verification.json`，EXE SHA-256 `1AEF8765…` |
| 发布产物启动 | 播放 result.mp4，进程 PerMonitorV2、窗口 DPI 144、视频表面 1658×1084；`CloseMainWindow` 后按渲染上下文 → EGL/SwapChain/D3D11 → mpv 会话释放，退出码 0；系统无 `mpv.exe` 进程 |

4 项跳过为需要显式设置素材环境变量的真实 4K/TS 测试（HEVC、AV1、PQ 渐变、TS 跳转对照），2026-09-11 与 2026-09-12 已在本机分别执行并通过，报告位于 `artifacts/hdr-4k/hardware-reports/` 与 `artifacts/rtx3070-analysis/101312-01da9b11/local-ts-comparison/`。

## 闸口审计

| 闸口 | 自动化证据 | 人工证据 | 结论 |
|---|---|---|---|
| Gate A：libmpv 控制 | LibMpv 79 项：ABI 布局/UTF-8、100 次会话创建销毁、请求 ID 关联/超时/取消、本地 WAV、HTTP Range 与 404 恢复、**HLS 播放列表加载/暂停/跨分片 seek/EOF、缺失播放列表报错后会话可用**、进程内 libmpv 且无外部 `mpv.exe`、日志 URL 脱敏、EOF 位置对齐、恢复租约与终态隔离 | MVP GUI（2026-09-10）本地与 HTTP 播放/暂停/时间轴/404 恢复；RTX 3070 三轮 15+6 次加载、80+ 次 seek、20 次 EOF 均由真实事件同步 | 通过 |
| Gate B：SDR Render API | Rendering 49 项：真实 GPU 上红下蓝方向、64→96→128→64 resize、同一 core 重建上下文、SDR/PQ 表面切换保留暂停帧、10-bit/FP16 精度、渲染连续性（不能呈现时 0 VO 丢帧）、释放顺序、渲染线程守卫；应用内 5 场景故障恢复 | 无外部 mpv 窗口；RTX 3070 全屏 3840×2160 表面、LG 4K60 59.94 fps、Render ≤ 10.79 ms；150% DPI 两台机器视频表面等于物理客户区；连续 28 次窗口尺寸切换无异常；用户目视 SDR 色调映射画面正常 | 通过 |
| Gate C：XAML 覆盖与输入 | App 54 项：UI 派发、浮层保留、seek 竞争、关闭协调、终态命令拒绝、手势分类、自动隐藏 | 鼠标：按钮、时间轴拖动、双击全屏、音量；键盘：空格、左右、F11、Esc；全屏/最大化/窗口切换；150% DPI（1060 与 3070）；单显示器 | 通过（范围内）：真实触屏、100%/125%/200% DPI、双显示器未验证 |
| Gate D：4K、硬解与 HDR | 真实 HEVC Main10 4K D3D11VA/EGL/P010、AV1 回退、PQ 梯度 11 档码值、渲染连续性、TS 跳转对照 | RTX 3070 三轮：HDR 开/关与播放中切换、PQ 10-bit 输出、4K60 HDR 全屏稳态、EOF 面板保留、正常退出；视觉为用户目视 | 通过（范围内）：跨屏与长时间稳定性按用户决定不验证 |

## 对照 `docs/architecture.md` 第 15 节

| 条件 | 证据 | 结论 |
|---|---|---|
| 不启动外部 `mpv.exe` | `LibMpvHlsTests` 进程模块含 `libmpv-2.dll` 且系统 mpv 进程数不变；发布目录无 `mpv.exe`；发布产物运行时系统无 `mpv.exe` | 满足 |
| C# 稳定创建、控制、销毁 libmpv 会话 | 100 次循环测试；发布烟雾 3 次；RTX 3070 三轮均正常释放 | 满足 |
| 视频通过 Render API 显示在 `SwapChainPanel` | 真实 GPU 像素测试；三轮外机日志 `VO: [libmpv]` 与 SwapChainPanel 绑定 | 满足 |
| XAML 控件覆盖视频并接收触摸与鼠标 | 鼠标/键盘已验证；触摸无硬件 | 鼠标满足，触摸未验证 |
| 窗口、全屏、DPI、resize 无黑屏/错位/持续闪烁 | 三轮外机 28 次尺寸切换、全屏、150% DPI；resize 故障恢复测试 | 满足（150% DPI、单显示器） |
| 4K 流使用预期硬件解码路径 | HEVC/AV1 4K 均 `d3d11va` / `d3d11-egl` / `p010` | 满足 |
| SDR 色彩正确 | 色块方向与色值、PQ→SDR 色调映射黑位/单调性/高光测试；用户目视 | 满足（无仪器对照） |
| HDR 输出通过第 9 节验收 | PQ 10-bit / BT.2020 / 417 nit 输出、11 档 PQ 码值像素测试；用户目视高光/暗部/渐变 | 满足（用户目视，无截图或仪器） |
| 播放、暂停、seek、缓冲、结束、错误可靠同步到 UI | 后端事件测试（含缓冲队列溢出修复）、GUI 与外机日志 | 满足 |
| 连续创建/销毁与设备重建无稳定崩溃或泄漏 | 100 次会话循环；5 场景应用内重建；三轮外机 0 异常 | 满足 |

## 已验证硬件范围与未验证项

- 已验证：NVIDIA GTX 1060（SDR、软解/硬解、150% DPI）；NVIDIA RTX 3070 + 未认证 HDR 4K 120 Hz 显示器（HDR 开/关、4K60 硬解、150% DPI）。
- 未验证（用户决定跳过或缺少硬件）：双显示器/跨屏、长时间稳定性（>2 分钟连续）、真实触屏、100%/125%/200% DPI、Intel/AMD GPU、HDR 亮度与色彩的仪器测量、HLG 与 Dolby Vision 素材。
- 已知行为（2026-09-12 下午已修复）：MPEG-TS 无索引流精确 seek 后曾出现一批 HEVC 参考帧跳过日志；锁定 libmpv 加入 `mpv-demux-seek-skip-to-keyframe.patch` 后，同一文件同一目标 430 组错误降为 0，见 `evidence/ts-seek-keyframe-2026-09-12.md`。

## Phase 1 输入

- 「最近打开」仅保存在进程内；HLS 自适应码率、字幕渲染、播放列表等产品功能未在 Phase 0 范围。
- TS 跳转体验已通过 demuxer 层关键帧起读修复，未动解码路径与 exact seek；RTX 3070 观感待复测。
- 建议在 Phase 1 早期补触屏与 100%/200% DPI 的一次人工矩阵。
