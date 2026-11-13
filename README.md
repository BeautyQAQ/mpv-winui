# Veyra

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

2026-11-14 开发源码采用 **Veyra** 名称与新标志，并让窗口标题栏、顶部入口和底部控制条在播放闲置 3 秒后一起隐藏，见 [交互更新](docs/implementation/window-chrome-20261114.md) 与 [品牌说明](docs/implementation/branding-20261114.md)。下方 11 月 13 日冻结候选仍保留当时的 MpvShell 名称与行为，不包含这次增量。

**当前阶段：Phase 3 进行中；新版 UI 候选的 P3-03 本轮发布范围验收通过（2026-11-13）。** 新包包含跟随 Windows 主题的界面、图标控制条和链接弹窗，以及既有旧 Intel HEVC 兼容修复。Debug/Release 显式回归分别为 567/0/2、551/0/2，实际候选 DLL 为 551/0/2；两项跳过为 RTX 3070 不适用的旧 Intel 专用场景，严格 runner 原始 false 保留，另行评定本机范围通过。有限连续短测、最终目录播放操作、运行中浅深色切换、两种高对比度主题、两次正常退出及最近记录重启恢复已验，见 [本轮验收](docs/implementation/evidence/P3-03-01-ui-candidate-20261113.md)。P3-02 两小时长测及资源趋势仍暂缓，不将本轮交付写成整个 Phase 3 完成。

当前源码支持本地文件、HTTP/HTTPS 直链、基础 HLS、播放/暂停、停止后重播、0.25～4× 倍速、精确跳转、时间轴数字预览、画面拖动跳转/调音量及反馈、音量/静音、轨道选择、媒体信息、全屏和最近打开持久化。停止会卸载媒体并清空进度与媒体信息，保留地址供重播；倍速在本次运行中跨停止、换片和 EOF 保留。视频根据片源与 Windows HDR 状态使用 PQ 10-bit 输出或 SDR 色调映射；优先 D3D11VA，经 ANGLE 直接传递 GPU 帧，不可用时回退软件解码。最近打开保留最新 10 条，成功打开后保存到用户本地目录，提供清空入口。停止、倍速、最近打开持久化和时间轴/手势改进属于 Phase 2 源码改动，现有 Phase 1 发布包不包含这些功能。

2026-09-12 的 RTX 3070 验收记录确认：PerMonitorV2 下全屏表面为 3840×2160，4K60 HDR TS 稳态约 59.94 fps；HDR 开关切换、EOF 信息保留与正常关闭通过。TS 跳转的关键帧起读补丁也已通过外机复测。结论限于单显示器、150% DPI、短时段和用户目视，详见 [Phase 0 验收](docs/implementation/evidence/P0-11-01-phase-0-acceptance.md) 与 [TS 修复证据](docs/implementation/evidence/ts-seek-keyframe-2026-09-12.md)。

P2-05 已修复代码审查发现的慢启动加载关闭等待链，4 个真实应用生命周期探针通过。该次 Debug/Release 构建均 0 警告/错误，默认回归各 525 通过、0 失败、9 跳过，显式素材全量各 534 通过、0 失败、0 跳过，四轮均首次通过；148 项构建输入全部一致。最终 Release 完整 GUI 流程与两次正常退出通过，重启恢复最近记录且不自动播放，验收后已按原字节恢复历史。历史证据见 [P2-05 记录](docs/implementation/evidence/P2-05-01-phase-2-acceptance.md)；P2-05 当时未生成新发布包。

上一工作包 P2-04 的最终 Debug/Release 构建均 0 警告、0 错误，Debug 默认及 Release 全量复验各 525 通过、0 失败、9 跳过。首轮 Release 为 524 通过、1 失败、9 跳过，既有渲染连续性零丢帧断言实际观察到 2 帧；未改代码或断言，同一二进制单项和全量复验随后通过，首次失败及未证实根因保留。真实 Release 已验证最小窗口、轨道/字幕、全屏/Esc、EOF/重播、焦点/自动隐藏、停止、输入校验错误及正常退出，见 [P2-04 记录](docs/implementation/evidence/P2-04-01-player-interface.md)。[P2-03](docs/implementation/evidence/P2-03-01-timeline-and-gesture-feedback.md) 的两配置默认各 478 通过、[P2-02](docs/implementation/evidence/P2-02-01-recent-history.md) 的各 411 通过、P2-01 显式各 359 项及 Phase 1 包的显式各 307 项均保留各自源码身份。功能测试不代表性能通过；GTX 1060 / UU 的 P3-01 / PERF-02 本机调查已于 11 月 3 日获用户接受，940M / Intel 本机兼容验收已于 11 月 4 日收口，设备限制见当前进度。

2026-11-05 同步时补回本地未提交的 [RTX 3070 复测](docs/implementation/evidence/P3-00-02-rtx3070-retest.md)及 [P3-02 连续短测](docs/implementation/evidence/P3-02-01-continuous-short-test.md)记录和测试工具。相关源码已提交为 `28ffe46`，本轮有限连续测试已复验通过；两小时长测与资源趋势仍按用户要求暂缓。10 月 29 日结果只对应当时源码和候选，不替代当前合并版本验收。

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

**[下载当前候选测试包](artifacts/candidates/MpvShell-phase3-ui-candidate-20261113-233547-b4d96c.zip)**（本轮 RTX 3070 发布范围通过），另有 [对应源码快照](artifacts/candidates/MpvShell-phase3-ui-candidate-20261113-233547-b4d96c-source.zip)。新包来自干净 `2d44dbd`，包含系统主题 UI、Phase 2 功能及旧 Intel 兼容修复；来源、大小、内容核验和 SHA-256 见 [发布入口](docs/implementation/release-status.md)。11 月 5 日候选来自 `28ffe46`，不含新版 UI，作为历史包保留。本机运行日志在包外；本轮后补验收文档不在冻结源码快照中。ZIP 不随 Git 同步。

生成新的独立测试包（PowerShell 7，仓库根目录）：

```powershell
pwsh -File build/testing/publish-candidate.ps1 -HdrMediaDirectory artifacts/p3-00-retest-20261029/media-hdr -P104MediaDirectory artifacts/p3-00-retest-20261029/media-p1-04 -PackageLabel phase3-candidate
# 记录最终目录运行结果，将本机日志移到包外后核验内容并封包：
pwsh -File build/testing/seal-candidate-package.ps1 -PackageDirectory <返回的目录>
```

脚本在 `artifacts/candidates/` 创建带日期、时间与唯一标识的目录及源码快照，封包脚本核对原生/托管许可、素材和 ZIP 条目。输入素材须先生成并核验，步骤见 [测试与发布工具](build/testing/README.md)。`sourceDirty=true` 时必须使用对应快照，不能仅以基线提交替代；封包只证明内容一致性，GUI 或回归未通过时仍须明确标为待验证候选。原 Phase 1 脚本保留兼容入口。

完整解压后，直接运行 `MpvShell.App.exe` 使用默认日志；双击 `Start-Debug.cmd` 开启详细日志，测试后关闭应用，在 `Test-Notes.txt` 填写复现步骤，再运行 `Collect-Logs.cmd` 收集日志。包内 `README-测试说明.md` 提供具体操作。底层发布脚本仍为 `build/testing/publish-mvp.ps1`，生成单独目录时应显式传入 `-OutputDirectory`，其历史默认目录不代表当前发布入口。

## 验证与进度

- [Phase 3 工作包与验收标准](docs/implementation/phase-3-plan.md)
- [Phase 3 当前进度](docs/implementation/phase-3-progress.md)
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

应用默认诊断日志位于 `%LOCALAPPDATA%\MpvShell\logs`，原生媒体日志对网络地址脱敏，测试素材不随 Git 提交。尚未验收的跨屏、长期稳定性、真实触屏及其他 DPI 等范围见 [Phase 3 进度](docs/implementation/phase-3-progress.md#4-独立跟进与未验证范围)；“最近打开”保存于 `%LOCALAPPDATA%\MpvShell\recent-history.json`，历史功能验收见 [持久化记录](docs/implementation/evidence/P2-02-01-recent-history.md)。当前新版 UI 候选已在 Windows 10.0.26300 / RTX 3070 完成最终目录验收；GTX 1060 / UU 和 HD 4600 的既有接受结论保留各自源码及设备范围，不视为新候选跨设备复验。工程声明的最低 Windows 版本不等于最低版本实机认证；具体支持范围和已知限制见 [本轮发布验收](docs/implementation/evidence/P3-03-01-ui-candidate-20261113.md#6-支持范围与已知限制)。
