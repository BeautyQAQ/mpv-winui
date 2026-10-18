# MpvShell

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

**当前阶段：Phase 2 V1 播放器交互已在声明范围内通过（2026-10-18）。** P2-00～P2-05 已完成，最终源码回归和 Release 完整打开、控制、换片、停止/重播、退出/重启流程通过。详见 [Phase 2 计划](docs/implementation/phase-2-plan.md)、[完成进度](docs/implementation/phase-2-progress.md) 和 [P2-05 验收矩阵](docs/implementation/evidence/P2-05-01-phase-2-acceptance.md)。Phase 3 是后续工作，尚未启动；[Phase 1 验收](docs/implementation/evidence/P1-05-01-release-and-phase-1-acceptance.md) 与现有发布包身份保持不变，PERF-02 和额外硬件/长期验证独立保留。

当前源码支持本地文件、HTTP/HTTPS 直链、基础 HLS、播放/暂停、停止后重播、0.25～4× 倍速、精确跳转、时间轴数字预览、画面拖动跳转/调音量及反馈、音量/静音、轨道选择、媒体信息、全屏和最近打开持久化。停止会卸载媒体并清空进度与媒体信息，保留地址供重播；倍速在本次运行中跨停止、换片和 EOF 保留。视频根据片源与 Windows HDR 状态使用 PQ 10-bit 输出或 SDR 色调映射；优先 D3D11VA，经 ANGLE 直接传递 GPU 帧，不可用时回退软件解码。最近打开保留最新 10 条，成功打开后保存到用户本地目录，提供清空入口。停止、倍速、最近打开持久化和时间轴/手势改进属于 Phase 2 源码改动，现有 Phase 1 发布包不包含这些功能。

2026-09-12 的 RTX 3070 验收记录确认：PerMonitorV2 下全屏表面为 3840×2160，4K60 HDR TS 稳态约 59.94 fps；HDR 开关切换、EOF 信息保留与正常关闭通过。TS 跳转的关键帧起读补丁也已通过外机复测。结论限于单显示器、150% DPI、短时段和用户目视，详见 [Phase 0 验收](docs/implementation/evidence/P0-11-01-phase-0-acceptance.md) 与 [TS 修复证据](docs/implementation/evidence/ts-seek-keyframe-2026-09-12.md)。

P2-05 已修复代码审查发现的慢启动加载关闭等待链，4 个真实应用生命周期探针通过。Debug/Release 构建均 0 警告/错误，默认回归各 525 通过、0 失败、9 跳过，显式素材全量各 534 通过、0 失败、0 跳过，四轮均首次通过；148 项构建输入全部一致。最终 Release 完整 GUI 流程与两次正常退出通过，重启恢复最近记录且不自动播放，验收后已按原字节恢复历史。实际证据见 [P2-05 记录](docs/implementation/evidence/P2-05-01-phase-2-acceptance.md)；本轮未生成新发布包，Phase 1 包身份不变。

上一工作包 P2-04 的最终 Debug/Release 构建均 0 警告、0 错误，Debug 默认及 Release 全量复验各 525 通过、0 失败、9 跳过。首轮 Release 为 524 通过、1 失败、9 跳过，既有渲染连续性零丢帧断言实际观察到 2 帧；未改代码或断言，同一二进制单项和全量复验随后通过，首次失败及未证实根因保留。真实 Release 已验证最小窗口、轨道/字幕、全屏/Esc、EOF/重播、焦点/自动隐藏、停止、输入校验错误及正常退出，见 [P2-04 记录](docs/implementation/evidence/P2-04-01-player-interface.md)。[P2-03](docs/implementation/evidence/P2-03-01-timeline-and-gesture-feedback.md) 的两配置默认各 478 通过、[P2-02](docs/implementation/evidence/P2-02-01-recent-history.md) 的各 411 通过、P2-01 显式各 359 项及 Phase 1 包的显式各 307 项均保留各自源码身份。功能测试不代表性能通过，PERF-02 仍需跟进。

## 构建与运行

需要 Windows x64、.NET 10 SDK 及 Windows App SDK 构建环境。原生 DLL 已固定版本和 SHA-256，随应用输出复制。

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
& .\src\MpvShell.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\MpvShell.App.exe
```

点击“打开文件”，或输入完整本地路径、HTTP/HTTPS 媒体直链后点击“打开地址”。也可把一个媒体路径作为应用启动参数。

快捷键：空格播放/暂停，左右方向键跳转 5 秒，F11 或双击画面切换全屏；Esc 依次取消当前预览/交互、关闭浮层、退出全屏。时间轴支持悬停数字预览、点击、拖动及键盘操作；画面横向拖动跳转，纵向拖动调音量，松手提交并显示反馈。未知时长或不可跳转时禁用时间轴操作，已知时长仍显示实际进度。

控制区按窗口宽度切换单行/双行，主控件至少 48 像素高；字幕与音轨分组并显示当前选择，字幕提供关闭项。加载、缓冲、EOF、停止和错误共用一个状态提示；播放时仅在窗口活动、无浮层或待完成操作、鼠标离开控制区且没有键盘操作焦点时闲置隐藏。

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

- [Phase 2 工作包与验收标准](docs/implementation/phase-2-plan.md)
- [Phase 2 当前进度](docs/implementation/phase-2-progress.md)
- [Phase 1 工作包与验收标准](docs/implementation/phase-1-plan.md)
- [Phase 1 完成记录与限制](docs/implementation/phase-1-progress.md)
- [当前发布包入口](docs/implementation/release-status.md)
- [Phase 0 进度与验收历史](docs/implementation/phase-0-progress.md)
- [SDR MVP 历史记录](docs/implementation/mvp-progress.md)
- [HDR/4K 实施与硬件记录](docs/implementation/hdr-4k-progress.md)
- [架构与原始验收要求](docs/architecture.md)
- [HTTP 与应用恢复测试工具](build/testing/README.md)

应用默认诊断日志位于 `%LOCALAPPDATA%\MpvShell\logs`，原生媒体日志对网络地址脱敏，测试素材不随 Git 提交。尚未验收的跨屏、长期稳定性、真实触屏及其他 DPI 等范围见 [Phase 2 进度](docs/implementation/phase-2-progress.md#6-独立跟进与未验证范围)；“最近打开”保存于 `%LOCALAPPDATA%\MpvShell\recent-history.json`，本地持久化、启动恢复和清空已通过自动测试及真实 GUI 重启验收；本轮实际结果见 [持久化记录](docs/implementation/evidence/P2-02-01-recent-history.md)。当前已验收包仍为 Phase 1，不包含本阶段源码改动。
