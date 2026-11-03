# P3-00 最终候选目录 GUI 验收

> 日期：2026-11-03；本次要求的六项 GUI 流程通过。
> 本篇只记录 GUI 轮；后续本机完整回归见 [回归与诊断](P3-00-03-current-machine-regression.md)。P3-00 整体仍待验证，2026-10-28 旧设备原生崩溃/性能挂起未关闭。

## 1. 产物与运行身份

旧候选 `MpvShell-phase3-candidate-20261028-121903-72991b` 的 ZIP 未随 Git 到本机。本次按既有发布流程重新生成独立候选，未覆盖旧身份；旧候选的失败记录见 [10 月 28 日基线记录](P3-00-01-baseline-and-candidate.md)。

| 项目 | 本轮实际值 |
|---|---|
| 候选名 / ID | `MpvShell-phase3-candidate-20261103-170050-5f9ed2` / `5f9ed232a0ad4bd5838780e1c72c12b9` |
| 构建来源 | 干净 `main / 18a85abe57d34abe3f59b45e623b50536a714a6a`，`sourceDirty=false`；发布前冻结 290 份源码，构建后及 GUI 后逐项核对一致 |
| 配置 | Release / `win-x64`，.NET 10.0.14 与 WinUI 自包含；SDK 10.0.403，打包 PowerShell 7.6.5 |
| 主 ZIP | [候选 ZIP](../../../artifacts/candidates/MpvShell-phase3-candidate-20261103-170050-5f9ed2.zip)，134,880,974 字节，693 个文件条目逐项核验 |
| 主 ZIP SHA-256 | `050A3DBEC608F0DB27CD789D40C28333695DDEEA1CF99EB81EBE633194578861` |
| 源码 ZIP | [290 文件快照](../../../artifacts/candidates/MpvShell-phase3-candidate-20261103-170050-5f9ed2-source.zip)，SHA-256 `083E7E75106B0DDED6B8650CFBB81760CCE4F605050234BA7154BA57326CC2D8` |
| App EXE / DLL SHA-256 | `7C0EA09BB1203B81916DB0D2AA9C6D4C9089188D3A3BC5D7A5D855E99327EC70` / `0359A2C66482293781DE1AC275A9AB1BC15F40D0D0FCC4DC1169262E6F33CA1B` |
| LibMpv 托管 DLL SHA-256 | `633DB8BE6E4DBFBB85518FFF016255113DC89D9B2F398362B55AD8CD552304A9` |
| 发布检查 | 命令退出 0；四份原生 DLL 哈希、AMD64、实际加载与 Client API 2.5 通过；临时和最终目录各 3 次会话创建/初始化/销毁通过 |
| 封包检查 | 35 份原生许可来源、26 个 NuGet 包、15 份素材、源码快照及 SourceBuild/通知一致性通过；命令退出 0 |
| 设备 | Windows 11 专业版 10.0.26300；GTX 1060 5GB，驱动 32.0.15.8266；另有 GameViewer Virtual Display Adapter |
| 会话 / 显示 | 用户经 UU 连接，Windows `console` 会话；桌面 1920×1080 / 119 Hz，播放器窗口约 1106×754；不据桌面刷新率判定播放器性能 |

所有 GUI 进程均来自上述最终目录，未运行 `bin/Release` 或旧候选。前两轮使用包内 `Start-Debug.ps1`，第三轮从同一 EXE 无参数启动，将日志直接写到包外。`Start-Debug` 只表示详细日志，实际为 Release，未启用 D3D11 调试层。源代码、测试和构建配置相对 `b9e4d2b` 未变；本次未修改产品代码。

## 2. 实际 GUI 结果

通过 Computer Use 的 `@oai/sky` 操作应用，并结合实际截图、重新观察后的 UI 状态、进程身份及日志核对。点击后即时树可能仍是过渡状态，结论以随后观察及日志为准。

| 场景 | 实际操作与结果 |
|---|---|
| 播放、暂停/继续 | 从地址栏打开包内 `TestMedia/4k/hevc-main10-hdr10-4k30.mkv`；出现实际灰阶动态图案，时间推进。暂停于约 7.2 秒后进度保持，按钮变为“播放”；继续后恢复。多轨样片显示动态彩色图案及变化的帧号 |
| 停止/重播 | 首轮验证 EOF 后停止；补充轮在多轨视频约 4.6/12.032 秒、仍在播放时停止。停止完成后黑屏、“已停止”、时间 `00:00 / 00:00`、时间轴禁用，地址保留；点击播放从头重新打开，进度再次推进 |
| 倍速 | `0.5×` 跨停止/重播/换片保持；日志确认 `speed=0.500000`。切换 `2×` 后重播 12.032 秒样片，17:05:07.755 加载、17:05:13.827 EOF，约 6.1 秒。补充轮 `0.25×` 生效且跨停止/重播/换片保持；不作为独立性能基准 |
| 换片 | HEVC 8 秒 → `TestMedia/p1-04/multitrack.mkv` 12.032 秒，标题、时长、画面更新。补充轮在多轨仍播放时换到 HEVC，旧媒体终止，新标题/8 秒时长/灰阶画面与播放进度正常 |
| 最近记录保存与重启恢复 | 首轮保存顺序为 multitrack、HEVC。正常关闭后无参数重启，空媒体、`00:00 / 00:00`，未自动播放；“最近”恢复两条记录及原顺序。点击 HEVC 条目能播放，重新观察列表确认 HEVC 置顶且无重复；磁盘 JSON 与列表一致 |
| 正常退出 | 三轮均点击标题栏关闭，退出码均 0、无残留进程。前两轮关闭发生在 EOF 后；补充轮在 HEVC 约 5.167/8 秒关闭，日志显示关闭流程暂停/挂起视频，随后释放渲染上下文、EGL/SwapChain/D3D11、销毁会话并完成主窗口关闭 |

| 轮次 / PID | 启动与结束（本地 UTC+08:00） | 退出与证据 |
|---|---|---|
| 首轮 / 2268 | 17:02:33 → 17:05:46 | `Exited / 0`；`gui-logs/20261103-170233-2fa5a62b23774568b56cedd82281d2ed/` |
| 无参重启 / 7924 | 17:05:56 → 17:07:43 | `Exited / 0`；`gui-logs/20261103-170556-c82847ff9b494ac996b98fcd31afb7fe/` |
| 播放中操作补充 / 23476 | 17:10:35 → 17:13:10 | `0`；`run3-identity.json`、`run3-exit.json`、`run3-logs/` |

补充轮首次点击出现工具错误 `coordinate input geometry is unavailable`，重新获取窗口与截图后重试成功；未出现播放器操作失败，也未通过强制结束代替正常退出。

## 3. 原始证据、历史保护与封包

原始证据统一在 `artifacts/p3-00/gui-20261103-final/`：

- `environment-and-history.json`、`candidate-build-info.json`、`publish.log` / 退出码、`seal.log` / 退出码及 `binary-identities.json` 固定设备、源码、构建与二进制身份。
- `gui-observations.json` 保存三轮分步 UI 树、窗口身份、时间及工具截图 ID；实际截图在 Computer Use 会话中检查，未另行导出图片文件。
- `process-1.json`、`process-2.json`、`launch-{1,2}.log`、运行目录内 `run-info.json` 和会话日志确认前两轮完整运行与正常退出；第三轮独立记录，日志直接写包外。
- 前两轮日志经包内 `Collect-Logs.ps1` 成功收集为候选目录旁的 `MpvShell-phase3-candidate-Logs-20261103-170800-4de6e67f.zip`。本机 `logs/` 已移到证据目录，没有进入交付 ZIP。
- 测试前备份用户 `%LOCALAPPDATA%/MpvShell/recent-history.json`，各轮保存测试结果后恢复原始字节。最终 SHA-256 与测试前均为 `024E43BF323BD590229148F46E831F64EB114AAC28F44E8F6C9CA7C0932A408F`；见 `cleanup.json`，测试条目未留在用户历史。

主 ZIP 内的 `GUI验收结果.md`、`gui-acceptance.json`、`Test-Notes.txt` 登记前两轮通过结果及其 EOF 后关闭边界。第三轮发生在封包之后，单独补齐播放中停止/换片/关闭证据；未修改封包目录，复核内容清单与 ZIP SHA-256 均保持一致，不重写已登记 ZIP。源码快照是构建时的干净提交，不包含本篇后补文档。

## 4. 完成范围与下一步

本次用户要求的最终候选目录六项 GUI 验收已完成并通过。当前候选与 10 月 28 日候选分别在 [发布入口](../release-status.md) 登记，旧工具绑定失败保留为历史，不再列为本机 GUI 待办。

本 GUI 轮没有重跑完整回归。后续本机 Debug/Release 默认各 525/0/9、显式各 534/0/0，验证工具与异步测试修正及原失败分别记于 [本机回归](P3-00-03-current-machine-regression.md)。用户确认旧 940M/Intel 设备暂不可访问，先完成本机验证；旧设备两配置显式原生崩溃与原 Debug 自然播放挂起仍未闭环，P3-00 整体继续待验证，待原机可用后定位与复验。

GTX 1060 / UU 的 P3-01 / PERF-02 调查已有独立用户接受记录；本次不重复其结论。长期稳定性、听音、真实触屏、跨屏/DPI、其他硬件及 HDR 仪器验证保持原有范围。
