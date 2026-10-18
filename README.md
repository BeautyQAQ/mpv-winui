# MpvShell

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

**当前阶段：Phase 0 与 Phase 1 已在声明范围内通过（2026-10-18）。** P1-00～P1-05 已完成固定回归、状态与错误契约、生命周期、视频 HLS/字幕验收和最终发布。PERF-02 与额外硬件/长期验证独立保留；后续产品交互工作另行规划。详见 [Phase 1 验收](docs/implementation/evidence/P1-05-01-release-and-phase-1-acceptance.md)、[进度与限制](docs/implementation/phase-1-progress.md)。

已支持本地文件、HTTP/HTTPS 直链、基础 HLS、播放/暂停、精确跳转、音量/静音、轨道选择、媒体信息和全屏。视频根据片源与 Windows HDR 状态使用 PQ 10-bit 输出或 SDR 色调映射；优先 D3D11VA，经 ANGLE 直接传递 GPU 帧，不可用时回退软件解码。

2026-09-12 的 RTX 3070 验收记录确认：PerMonitorV2 下全屏表面为 3840×2160，4K60 HDR TS 稳态约 59.94 fps；HDR 开关切换、EOF 信息保留与正常关闭通过。TS 跳转的关键帧起读补丁也已通过外机复测。结论限于单显示器、150% DPI、短时段和用户目视，详见 [Phase 0 验收](docs/implementation/evidence/P0-11-01-phase-0-acceptance.md) 与 [TS 修复证据](docs/implementation/evidence/ts-seek-keyframe-2026-09-12.md)。

最终源码复核（2026-10-18，`d7522cb` 上的工作区快照）：Debug/Release 构建各 0 警告、0 错误；默认回归各 298 通过、9 跳过，显式提供全部媒体输入后各 **307 通过、0 失败、0 跳过**。新 Release 包的原生/许可/源码快照、最终目录播放退出和 ZIP 校验均通过。功能测试不代表性能通过：本轮 RDP / 4K 离屏无读回测量约 10.67 fps，PERF-02 仍需跟进。

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

**[当前发布包入口](docs/implementation/release-status.md)** 登记 2026-10-18 的 Phase 1 Release x64 自包含包（约 135 MB）、对应源码快照、哈希与验证范围，附三份 4K 样片及 P1-04 小样本。691 个包文件逐项核验，两次最终目录运行均退出 0。此前 GTX/RTX 包及用户复测保留各自历史身份；`artifacts/mvp/` 等历史目录不是最新版本入口。

生成新的独立测试包（PowerShell 7，仓库根目录）：

```powershell
pwsh -File build/testing/publish-phase1.ps1 -HdrMediaDirectory artifacts/p1-05/media -P104MediaDirectory artifacts/p1-04/media-final
# 从返回的目录完成播放/退出验证，移出本机日志后封包：
pwsh -File build/testing/seal-phase1-package.ps1 -PackageDirectory <返回的目录>
```

脚本在 `artifacts/phase-1/` 创建带时间与唯一标识的目录及源码快照，封包脚本核对原生/托管许可、素材和 ZIP 条目。输入素材须先生成并核验，步骤见 [测试与发布工具](build/testing/README.md#phase-1-发布与封包)。`sourceDirty=true` 时必须使用对应快照，不能仅以基线提交替代；生成完成不自动等于已验收。

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
