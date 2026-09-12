# MpvShell

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

**当前阶段：Phase 0 已在声明范围内通过，Phase 1 工作包已建立。** Phase 1 聚焦生产级播放基础：固定回归、状态与错误契约、生命周期、视频 HLS/字幕验收和发布流程。当前工作包与下一步以 [Phase 1 进度](docs/implementation/phase-1-progress.md) 为准，范围与完成标准见 [Phase 1 计划](docs/implementation/phase-1-plan.md)。

已支持本地文件、HTTP/HTTPS 直链、基础 HLS、播放/暂停、精确跳转、音量/静音、轨道选择、媒体信息和全屏。视频根据片源与 Windows HDR 状态使用 PQ 10-bit 输出或 SDR 色调映射；优先 D3D11VA，经 ANGLE 直接传递 GPU 帧，不可用时回退软件解码。

2026-09-12 的 RTX 3070 验收记录确认：PerMonitorV2 下全屏表面为 3840×2160，4K60 HDR TS 稳态约 59.94 fps；HDR 开关切换、EOF 信息保留与正常关闭通过。TS 跳转的关键帧起读补丁也已通过外机复测。结论限于单显示器、150% DPI、短时段和用户目视，详见 [Phase 0 验收](docs/implementation/evidence/P0-11-01-phase-0-acceptance.md) 与 [TS 修复证据](docs/implementation/evidence/ts-seek-keyframe-2026-09-12.md)。

最近一次源码复核（`f793d0b`，2026-09-12）：Debug/Release 构建均 0 警告、0 错误；Debug 显式提供四项媒体样本后 **188 通过、无跳过**，Release 常规回归 **184 通过、4 项媒体测试跳过**。这些测试证明功能与像素通路，不代表长时间或全场景性能通过，详见 [Phase 1 基线证据](docs/implementation/evidence/P1-00-01-baseline-and-release-entry.md)。

## 构建与运行

需要 Windows x64、.NET 10 SDK 及 Windows App SDK 构建环境。原生 DLL 已固定版本和 SHA-256，随应用输出复制。

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
& .\src\MpvShell.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\MpvShell.App.exe
```

点击“打开文件”，或输入完整本地路径、HTTP/HTTPS 媒体直链后点击“打开地址”。也可把一个媒体路径作为应用启动参数。

快捷键：空格播放/暂停，左右方向键跳转 5 秒，F11 或双击画面切换全屏，Esc 退出全屏/关闭浮层。时间轴支持点击、拖动及键盘操作。

## 当前测试包与发布

**[当前发布包入口](docs/implementation/release-status.md)** 统一登记可用 ZIP、解压目录、启动方法、源码提交、哈希及验证范围。当前登记的是包含 TS 修复的 Release x64 自包含测试包；`artifacts/mvp/`、`artifacts/hdr-4k/win-x64/` 和 `artifacts/phase-0/win-x64/` 是历史产物，不作为最新版本入口。

生成新的独立测试包（PowerShell 7，仓库根目录）：

```powershell
pwsh -File build/testing/publish-rtx3070.ps1 -IncludeTestMedia
```

脚本在 `artifacts/rtx3070/` 下创建带时间与唯一标识的目录和 ZIP，包含 .NET/WinUI 运行时、许可证、符号及构建清单。`-IncludeTestMedia` 需要已生成并核验的三份 4K 合成样片；不附带样片时省略该参数。生成完成后按发布入口中的核验规则登记，不能仅凭目录时间把新包视为已验收。

完整解压后，直接运行 `MpvShell.App.exe` 使用默认日志；双击 `Start-Debug.cmd` 开启详细日志，测试后关闭应用，在 `Test-Notes.txt` 填写复现步骤，再运行 `Collect-Logs.cmd` 收集日志。包内 `README-测试说明.md` 提供具体操作。底层发布脚本仍为 `build/testing/publish-mvp.ps1`，生成单独目录时应显式传入 `-OutputDirectory`，其历史默认目录不代表当前发布入口。

## 验证与进度

- [Phase 1 工作包与验收标准](docs/implementation/phase-1-plan.md)
- [Phase 1 当前进度](docs/implementation/phase-1-progress.md)
- [当前发布包入口](docs/implementation/release-status.md)
- [Phase 0 进度与验收历史](docs/implementation/phase-0-progress.md)
- [SDR MVP 历史记录](docs/implementation/mvp-progress.md)
- [HDR/4K 实施与硬件记录](docs/implementation/hdr-4k-progress.md)
- [架构与原始验收要求](docs/architecture.md)
- [HTTP 与应用恢复测试工具](build/testing/README.md)

应用默认诊断日志位于 `%LOCALAPPDATA%\MpvShell\logs`，原生媒体日志对网络地址脱敏，测试素材不随 Git 提交。尚未验收的跨屏、长期稳定性、真实触屏及其他 DPI 等范围见 Phase 1 进度；“最近打开”仅保存在进程内，停止、倍速和交互完善列入后续产品工作。
