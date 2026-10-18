# P2-05 Phase 2 阶段验收

> 状态：已完成；P2-05 与 Phase 2 在声明范围内通过。关闭等待链修复、4 个真实生命周期探针、两配置构建/默认/显式全量、最终 Release 完整 GUI 与身份清理均通过。
> 日期：2026-10-18
> 对应计划：[P2-05 工作包](../phase-2-plan.md#p2-05phase-2-验收)
> 当前进度：[Phase 2](../phase-2-progress.md)

## 1. 验收范围与身份

P2-05 对 P2-01～P2-04 的最终实现进行阶段验收，复验完整打开、控制、换片、停止/重播与退出/重启流程，并确认 Phase 1 的播放基础没有退化。基线仍为 `main / 891d8597e5df57e8d3376618f4f6c2c7412da241` 加本阶段未提交工作区；最终源码、构建二进制、媒体输入与 GUI 进程应分别登记，不能仅用基线提交替代工作区身份。

本轮证据目录为 `artifacts/p2-05/`，保存实际执行入口、源码及输入哈希、Debug/Release 日志、默认/显式 TRX、媒体功能/性能报告与最终 GUI 记录。本轮执行、继承证据和未验证边界分别登记，结论依据实际报告。

开始时的 `source-identity.json` 属于关闭修复前输入；修复后由 `run-acceptance.ps1` 固定 `source-identity-before-build.json`，共 148 项，包含 `build/testing/test-app-recovery.ps1`。初始记录不能替代修复后验收身份。`environment.json` 记录 Windows 11 10.0.26300、.NET SDK 10.0.402、PowerShell 7.6.5，系统列出 GTX 1060 5GB / 驱动 32.0.15.8266 和 GameViewer Virtual Display Adapter；实际渲染适配器与测量环境以本轮运行报告为准。`phase1-package-identity.json` 确认 Phase 1 包和源码 ZIP 的 SHA-256 均与发布登记相符，这次只核对身份，不重复声称已重新逐项验证 ZIP 内容。

四轮自动回归结束后的 `build-identity.json` 确认全部 148 项构建输入一致，记录 Debug/Release 的 exe/dll SHA-256。Release 二进制最后写入时间为 16:22:08，早于最终 GUI 的 16:27:43 和 16:47:57 两次启动。`closure-verification.json` 再次核对 148/148 输入与 4/4 二进制哈希一致，GUI 正常释放、进程退出及历史恢复完成；本轮解决方案文档索引在构建前已加入，没有构建后身份差异。四轮共 16 份 TRX 的逐项统计见 `regression-summary.json`，验收脚本退出 0，没有失败重跑。

本轮没有生成新的发布包。当前已登记产物仍为 [Phase 1 Release 自包含包](../release-status.md)，P2-05 源码验证不改变其内容、来源或包内功能。PERF-02、真实触屏、额外 DPI/显示器/硬件及长期验证独立保留，阶段功能验收不等于 Phase 3 完整兼容性与发布验收；Phase 3 尚未启动。

## 2. 继承证据：属于原工作包，不代替本轮执行

| 输入 | 历史源码与自动回归 | 已有真实流程与限制 |
|---|---|---|
| [P2-01 停止与倍速](P2-01-01-stop-and-speed.md) | 129 文件源码身份；两配置默认各 350 通过、9 跳过，显式各 359 通过、0 失败/跳过 | Debug 默认宽窗验证 0.25×、EOF/播放中停止、重播和正常退出；GUI 早于最后的后端停止期 seek 防护补丁，该补丁由最终自动回归覆盖。加载取消/旧回复/卸载确认主要依据自动测试 |
| [P2-02 最近打开](P2-02-01-recent-history.md) | 135 文件源码身份；两配置默认各 411 通过、0 失败、9 跳过，历史相关各 62 项通过 | 最终 Release 恢复顺序且不自动播放、点击重新打开、清空、再次重启仍为空及正常关闭；长度上限、10 条去重、存储失败及竞态由自动测试覆盖 |
| [P2-03 预览与手势](P2-03-01-timeline-and-gesture-feedback.md) | 140 文件最终身份；两配置默认各 478 通过、0 失败、9 跳过 | 点击/拖动、悬停与实际位置分离、键盘、画面横纵拖动、反馈、Esc、最小化恢复及闲置隐藏已验；最终构建另复验 EOF 满进度和停止归零。合成手势不等于 Windows 触控注入或真实触屏 |
| [P2-04 界面完善](P2-04-01-player-interface.md) | 执行 manifest 147 项，收尾 146 项一致，唯一差异为解决方案文档索引；最终 Debug 默认和 Release 全量复验各 525 通过、0 失败、9 跳过 | 最终 Release 验证 720×640、长标题/轨道、选择/关闭字幕、滚动、全屏/Esc、焦点/悬停保持、闲置隐藏、EOF/重播、停止、输入校验错误及正常退出。首轮 Release 524/1/9 失败与后续复验保留 |
| [P1-05 / Phase 1](P1-05-01-release-and-phase-1-acceptance.md) | 独立包与 257 文件源码快照；两配置默认各 298 通过、9 跳过，显式各 307 通过、0 失败/跳过 | HLS、轨道/字幕像素、媒体信息、硬解/回退、PQ、TS 与测量工具已有输入证据；最终目录运行/许可/ZIP 核验属于 Phase 1 包。性能约 10.67 fps 是已测量、未判定，不代表 4K60 流畅 |

各旧记录保留当时“下一包未开始”的历史上下文；当前阶段状态以本记录和 [进度表](../phase-2-progress.md) 为准。旧 GUI、默认回归、显式媒体结果分别属于对应源码与运行环境，不合并为一次“所有场景已经通过”。

## 3. 最终交互验收矩阵

| 验收条件 | 既有自动/GUI 依据 | P2-05 本轮复验与当前结果 |
|---|---|---|
| 打开本地媒体及 HTTP/HTTPS，成功/失败/取消归属正确 | Phase 1 HTTP/HLS、P2-01 加载与旧结果隔离；P2-04 加载/错误状态卡 | 两配置全量及 HTTP/HLS 显式场景通过；本轮 GUI 通过原生文件选择器打开中文长名 MKV、地址框打开 MP4。未扩展互联网或浏览器认证场景 |
| 播放/暂停、音量/静音和常用控制入口可操作 | 既有 ViewModel 与原生控制回归；P2-03 音量手势、P2-04 控件状态及 Space | 两配置全量通过；本轮 GUI Space 暂停、33% 音量、静音、0.25× 和重播操作均与显示一致 |
| 播放/暂停/加载/EOF 停止，重复停止、停止后重播 | P2-01 卸载确认、黑帧、加载取消和事件屏障；P2-04 重播加载态/取消 | 两配置全量通过；本轮 GUI 停止归零/禁用/信息清空，Play 重播恢复画面时钟。加载/EOF 停止与竞态由最终自动测试和前序 GUI 对应证据覆盖 |
| 倍速合法范围、实际回传及停止/换片/EOF 保留 | P2-01 后端与 ViewModel 测试、0.25× GUI 时长观察 | 两配置全量通过；本轮 GUI 0.25× 跨换片保留，停止/重播控制链通过；重启恢复 1×，不要求跨进程保存倍速 |
| 最近打开成功后保存、去重/上限、恢复不自动播放 | P2-02 存储/ViewModel、实际重启恢复/重新打开 | 两配置全量通过；本轮无参重启为空闲、两条顺序恢复正确，点击 MKV 播放并去重置顶。10 条/最长地址由自动测试覆盖 |
| 最近打开清空、失败可恢复、旧任务不复活 | P2-02 真实清空再重启、损坏/不可写/关闭等待与竞态测试 | 两配置全量通过；清空后重启 GUI 沿用 P2-02 证据，本轮未重复清空流程，不将历史还原当成产品清空操作 |
| seek 预览与实际位置分离、悬停/拖动/键盘目标一致 | P2-03 协调层、ViewModel、seekable 原生测试与真实鼠标/键盘 | 两配置全量通过；本轮 GUI 鼠标定位约 60.5 秒，Right 预览/提交至约 65 秒。未知时长/越界/旧位置事件由自动测试核对 |
| 手势阈值、锁轴、节流、取消与反馈 | P2-03 横向/纵向鼠标手势、可控时钟与生命周期回归 | 两配置全量通过；完整横纵拖动 GUI 沿用 P2-03 证据，本轮只复验实际执行的常用键鼠控制，不将合成 delta 测试称为触屏通过 |
| 换片清理旧进度/轨道/信息/缓冲，旧结果不回填 | P2-01 控制版本、P2-03 取消、Phase 1 媒体信息与换片回归 | 两配置全量及显式媒体信息清理通过；本轮 GUI 从双音轨 MKV 换到 MP4，新进度从 0 开始，无音轨/无字幕，H264 320×180 / 8 bit / 10 fps，旧数据清空 |
| 音轨/字幕分组、关闭字幕、实际选择和焦点稳定 | P2-04 长名称/选择/列表滚动，Phase 1 原生轨道与字幕像素 | 两配置全量及显式字幕像素回归通过；本轮 GUI English 音轨 2 与关闭字幕选中正确，换片后列表清空；最小窗口滚动/焦点细项沿用 P2-04 |
| 单一状态提示、EOF 保留末尾进度、停止归零 | P2-03 EOF 修复，P2-04 状态卡/重播/停止/输入错误 | 两配置全量通过；本轮 GUI 加载/停止/重播链通过。EOF 与输入错误 GUI 沿用 P2-04；后端网络错误/缓冲恢复由本轮显式场景验证 |
| 宽窄窗口、全屏、Esc 优先、鼠标与键盘焦点/自动隐藏 | P2-04 720×640、双 Esc、鼠标悬停、键盘焦点至少 12 秒及闲置隐藏 | 两配置全量通过；本轮 F11 全屏、首次 Esc 关面板保持全屏、再次 Esc 退出，重启播放后自动隐藏、Space 唤回并暂停。最小窗口与焦点保持细项沿用 P2-04，未扩大 DPI 矩阵 |
| 正常退出释放资源，重启恢复且无残留测试历史 | P2-02 关闭等待保存、P2-03/04 顺序释放和历史恢复 | 新启动慢加载 5 秒探针及其余 3 个生命周期场景通过；本轮两次 Release 正常退出/重启与释放链通过，两个进程均消失，历史按原字节恢复且哈希相等 |

## 4. Phase 1 播放基础与显式素材矩阵

本轮已在两种配置显式提供并核验既有固定输入，实际环境变量、素材路径/哈希与报告目录在 `artifacts/p2-05/media-inputs.json` 登记。P1-05 的历史 `regression-inputs.json` 作为素材来源，其历史提交字段不替代 P2-05 源码身份。

| 基础能力 | 继承范围 | P2-05 本轮要求与当前结果 |
|---|---|---|
| 状态/命令、错误隔离、生命周期与页面恢复 | P1-02、P1-03 及后续默认回归 | Debug/Release 默认全量各 525 通过、0 失败、9 跳过，四个额外真实应用探针通过；注入故障不等于真实驱动重置 |
| 视频 HLS、404 重载、缓冲恢复、字幕像素和换片信息 | P1-04 固定目录及四项显式测试 | 两配置四份报告均 Passed，实际移动像素、恢复、字幕像素与换片清理通过；不扩展未覆盖 HLS/字幕格式 |
| HEVC Main10 硬解、AV1 实际回退、PQ/SDR 像素 | P1-05 三份 4K 固定样本及已锁原生依赖 | 两配置 HEVC 实际 d3d11va、AV1 软件回退且输出非黑，PQ/SDR GPU 回读验证通过；AV1 各累计呈现丢帧 2。本机测试不等于新 GPU/显示器认证 |
| TS seek 产品硬解/软解与关键帧起读对照 | 已有合成 TS 与固定 seek 目标 | 两配置产品硬/软路径均无参考错误、最大偏差 0.021333 秒；legacy 对照各 192 错误组，offset=0 对照偏差 1.021333 秒，详见下表 |
| 自然播放性能测量链路 | 既有 4K60 样本、预热/稳态与无读回测量 | Debug/Release 本轮 offscreen 稳态约 60.0264/59.9810 fps，测量窗口 VO/解码丢帧增量均 0；报告仍为“已测量，未判定”，PERF-02 未关闭 |

默认运行时缺少显式输入的条件测试按实际结果记作跳过；显式运行必须核对这些测试是否实际执行。TRX 汇总以逐个 `UnitTestResult outcome` 为准，并与控制台对照，不只读取曾漏报跳过数的 `Counters.notExecuted`。

### 4.1. 显式功能报告

两配置原始报告分别位于 `{Debug,Release}-explicit-hardware/` 和 `{Debug,Release}-explicit-p1-04/`。P1-04 媒体清单 SHA-256 为 `B8ED0E000C4663A112E1E6548970EF4E0BAFAF7F8495F769B69C827392FF5FA9`，本轮报告中的 mpv SHA-256 均为 `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705`。以下均为本轮实际结果。

| 报告 | 两配置共同结果与差异 |
|---|---|
| `hevc-hardware-report.json` | HEVC Main10 HDR10、3840×2160、30 fps，`HardwareVerified=true`、`SoftwareFallback=false`；d3d11va / d3d11-egl / p010、GPU resident；各呈现 60 帧且非黑，解码/呈现累计丢帧均 0 |
| `av1-hardware-report.json` | AV1 10 bit 4K30 SDR，`SoftwareFallback=true`、`HardwareVerified=false`，decoder=no / yuv420p10、非 GPU resident；各呈现 60 帧且非黑，解码丢帧 0、呈现累计丢帧 2 |
| `hdr-gradient-pixel-report.json` | 两配置回读值相同：R10 直通斜坡 770 个红码、最高块约 990.0144 PQ nits；HDR tone-map 725 个红码、最高约 654.8003 nits；SDR B8 247 个红码、最高红 244、黑块 0。仅验证 GPU 回读/转换链路，不代表实体 HDR 屏亮度校准 |
| `video-hls-*.json` / `video-hls-buffering-*.json` | `MovingPixels=13388`；Playing/EOF 媒体信息保留；206/404、失效清单/分片及恢复通过，`NativeLogRedaction=true`、`EndReached=1`，`BackendFaulted=2` 对应预期错误场景。缓冲位置约 5.729667 秒，buffering true→false 后恢复 H264/AAC d3d11va |
| `multitrack-subtitles-*.json` / `media-information-*.json` | 英/中音轨和 SRT/ASS/关闭选项；SRT 顶/底变化像素 0/504、ASS 为 2288/0。纯音频 Video/Resolution/HDR 为空、pcm_s16le；纯视频 AudioCodec 为空，无旧媒体信息残留 |
| `ts-seek-decode-path-comparison.json` | 产品 d3d11va 与 no 两路径（offset=1、skip=yes）各 20 秒顺播和 8 次 seek，参考错误及 FrameDropCount 均 0、最大位置偏差 0.021333 秒。Debug 硬/软最大重启延迟 23.3/21.9 ms，Release 为 20.5/35.0 ms。legacy skip=no 对照各 192 错误组覆盖 8 次 seek；offset=0 对照错误组 0，但偏差 1.021333 秒，不能宣称所有对照均精确 seek |

### 4.2. 自然播放性能：已测量，未判定

两配置的 `performance-hevc-main-sdr-4k60-30s-sdr-d3d11va-vsync1.json` 使用同一 HEVC Main SDR 4K60 输入，3 秒预热、20 秒测量；输出为 SDR B8 3840×2160，d3d11va / nv12、GPU resident、vsync=1。实际适配器为 GTX 1060 5GB，Windows 10.0.26300，ANGLE 驱动 32.0.15.8266。测量窗口内无 GPU 读回，窗口后确认非黑；selfcheck 两配置均为 8 帧、3 次读回且非黑。

| 测量项 | Debug | Release |
|---|---:|---:|
| 稳态墙钟时间 | 20.0256442 秒 | 20.0408473 秒 |
| 呈现帧 / fps | 1202 / 60.0264 | 1202 / 59.9810 |
| Render P95 | 1.1463 ms | 1.1788 ms |
| Present P95 | 0.0858 ms | 0.0857 ms |
| 测量窗口 VO / 解码丢帧增量 | 0 / 0 | 0 / 0 |
| 测量窗口 delayed / skipped | 0 / 0 | 0 / 0 |
| 窗口结束时累计 VO 丢帧 | 1 | 2 |

两份报告的 `Status` 均为“已测量，未判定”，`MinimumPresentedFps` 和 `TargetFps` 均为 null。本轮约 60 fps 属于当前单一 offscreen Composition SwapChain 场景，没有绑定可见 visual；不能外推物理屏流畅度、完整 UI 或所有 4K60/HDR 场景。累计 VO 丢帧不为零，不能将稳态增量零写成全程零丢帧，也不以本轮功能/测量测试关闭 PERF-02。P1-05 历史约 10.67 fps 属于当时的独立运行环境，不替代本轮数据。

## 5. 本轮执行记录

执行入口为 `artifacts/p2-05/run-acceptance.ps1`：核对媒体输入并固定修复后源码快照，顺序执行 x64 Debug/Release 构建与默认/显式全量，构建和测试均使用 `-m:1` 控制项目级并发；Rendering 内部原 xUnit 并行规则及断言均未修改，也不据此推定 P2-04 首次丢帧失败的根因。Debug 构建后、全量测试前，调用真实应用恢复脚本运行四个生命周期场景。整个脚本退出 0，四轮回归均首次通过。

| 配置 / 执行 | 当前结果 | 证据 |
|---|---|---|
| 初始源码、环境与既有发布身份核对 | 已记录；Phase 1 包/源码 ZIP 哈希与登记一致 | `source-identity.json`、`environment.json`、`phase1-package-identity.json`；修复后源码与 GUI 收尾身份另在 `build-identity.json` / `closure-verification.json` 登记 |
| 启动参数慢加载时关闭及生命周期复验 | 四个 Debug 真实应用探针全部通过，各进程退出 0 | `lifecycle-probes.log`、`lifecycle-probes/summary-20261018-081806-333-7549866d636b4243af834d6b56356d38.json` 及各报告/日志；详细耗时见下表 |
| Debug x64 构建 | 0 警告、0 错误 | `Debug-build.log` |
| Debug 默认全量 | 525 通过、0 失败、9 跳过 | `Debug-default-tests.log`、`Debug-default-tests/`；App 297、Abstractions 2、LibMpv 156、Rendering 70 通过，Rendering 另 9 项条件跳过 |
| Debug 显式素材全量 | 534 通过、0 失败、0 跳过 | `Debug-explicit-tests.log`、`Debug-explicit-tests/`；App 297、Abstractions 2、LibMpv 156、Rendering 79 通过，9 个条件场景均实际执行；功能/测量报告另列 |
| Release x64 构建 | 0 警告、0 错误 | `Release-build.log` |
| Release 默认全量 | 525 通过、0 失败、9 跳过 | `Release-default-tests.log`、`Release-default-tests/`；项目分布与 Debug 默认一致 |
| Release 显式素材全量 | 534 通过、0 失败、0 跳过 | `Release-explicit-tests.log`、`Release-explicit-tests/`；项目分布与 Debug 显式一致，9 个条件场景均实际执行 |
| 最终 Release 完整 GUI | 声明的完整流程通过 | `gui-acceptance-summary.json`、两个进程 JSON/会话日志和各 `gui-*-tree.txt`；具体复验与继承范围见下文 |
| 构建/测试身份核对 | 148 项输入全部一致，已固定两配置二进制哈希 | `source-identity-before-build.json`、`build-identity.json`；未在 `--no-build` 测试阶段修改代码 |
| GUI 后身份与清理 | 148/148 输入、4/4 二进制一致，正常退出及历史恢复通过 | `closure-verification.json`、`gui-first-exit.json`、`gui-cleanup.json`；两份日志释放链完整且无未处理异常 |

本轮 GUI 使用最终 Release 构建；执行前备份用户最近记录并固定样本身份，结束后确认两个进程退出且原历史已恢复。清空记录、恢复排序、输入失败和原生网络失败分别描述，不将一种场景当成另一种已覆盖。

### 5.1. 真实应用生命周期

本轮真实应用生命周期探针使用 Debug、D3D11 调试层关闭，执行命令为：

```powershell
& ./build/testing/test-app-recovery.ps1 -NoBuild -D3D11DebugLayer Off -TimeoutSeconds 45 -Mode @('close-during-startup-load', 'close-during-load', 'page-rebuild-playing', 'page-rebuild-paused') -ReportDirectory artifacts/p2-05/lifecycle-probes
```

外层进程等待上限 45 秒与新增探针的关闭/加载结束共用 5 秒预算分开记录。

| Debug 场景 | 实际结果 | 说明 |
|---|---|---|
| `close-during-startup-load` | 整进程约 1.93 秒，退出 0 | 真实命令行位置参数打开 loopback 挂起 URL，启动加载 `pending=true`；`ShutdownSeconds=0.192`、`StartupLoadCompletionSeconds=0.193`，共用 5 秒预算。`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe`、`PendingLoadEnded` 均为 true |
| `close-during-load` | 整进程约 1.62 秒，退出 0 | 普通加载中关闭场景通过 |
| `page-rebuild-playing` | 整进程约 4.23 秒，退出 0 | 播放中页面卸载/重建通过 |
| `page-rebuild-paused` | 整进程约 4.17 秒，退出 0 | 暂停中页面卸载/重建通过 |

这些耗时均为修复后的结果；没有对修复前版本运行挂起启动 URL 探针，不构成修复前后耗时对照。

### 5.2. 最终 Release 完整 GUI

`gui-acceptance-summary.json` 按 12 项检查汇总真实操作及对应 UI 树、日志、进程和历史 JSON。两次均使用最终 Release：进程 5148 于 16:27:43 启动、16:47:44 正常关闭；进程 22840 于 16:47:57 无参数启动、16:50:53 正常关闭。窗口使用本机键鼠操作，截图在工具会话中目视检查，未保存为磁盘 PNG；持久证据为 `gui-*-tree.txt` 等文件。操作后即时 UIA 快照可能落后于画面，断言采用状态稳定后的记录。

`gui-inputs.json` 固定两份输入：中文长标题双音轨/内嵌字幕 MKV 来自 P2-04，SHA-256 为 `200A83D07E25E80DB3CA3E5D26BF21F82AF66045E094776D61D48CDF201EA2EE`；`timeline-120s.mp4` 来自 P2-03，SHA-256 为 `60157738F6063D36931DA14AD629F5211CB9934BA0D570B68901620E58612805`。

| 完整流程 | 本轮实际结果与证据 |
|---|---|
| 文件打开与控制 | 原生文件选择器打开中文长名 MKV，加载态及画面正常；Space 在约 23 秒暂停，0.25×、33% 音量与静音显示正确。见 `gui-file-open-loading-tree.txt`、`gui-pause-volume-mute-tree.txt` |
| seek、轨道与全屏 | 鼠标定位约 60.5 秒，Right 显示预览后提交至约 65 秒；English 音轨 2 和关闭字幕选中。F11 进入全屏，第一次 Esc 关面板并保持全屏，第二次退出。见 `gui-keyboard-seek-tracks-tree.txt`、`gui-track-selection-tree.txt`、两份 `gui-escape-*-tree.txt` |
| 地址换片 | 地址打开 MP4，新进度从 0 开始；旧音轨/字幕清空，显示无音轨/无字幕，信息为 H264、320×180、8 bit、10 fps；0.25×、33% 音量和静音保留。见两份 `gui-switch-media-*-tree.txt` |
| 停止与重播 | 停止后 00:00 / 00:00，进度/跳转/停止禁用，媒体信息清空；点击 Play 重新加载并恢复画面时钟。见 `gui-stopped-tree.txt`、`gui-replay-tree.txt` |
| 正常退出与无参重启 | 首次正常 Alt+F4 后资源释放链完整、进程消失。再次启动保持 idle、不自动播放，倍速/音量初始 1×/100%；最近仍有两条，MP4 在前、MKV 在后。见 `gui-first-session.log`、`gui-first-exit.json`、`gui-restart-idle-tree.txt`、`gui-restart-recent-tree.txt`、`gui-history-before-restart.json` |
| 最近重新打开与结束 | 点击 MKV 正常播放，控件闲置隐藏，Space 唤回并在约 24.7 秒暂停；最近记录去重并将 MKV 置顶。第二次正常退出释放链完整、进程消失。见 `gui-restart-recent-playing-tree.txt`、`gui-restart-paused-tree.txt`、`gui-recent-reordered-tree.txt`、`gui-history-after-reopen.json`、`gui-second-session.log` |

两份会话日志均确认 renderer、EGL/SwapChain/D3D11、session、window 按顺序完成释放，没有未处理异常。`gui-cleanup.json` 与 `closure-verification.json` 确认进程全退出、用户历史按原字节恢复，恢复后的 SHA-256 为 `024E43BF323BD590229148F46E831F64EB114AAC28F44E8F6C9CA7C0932A408F`。

本轮完整流程没有重做全部 P2-04 最小窗口/焦点保持细项、P2-02 清空历史重启或 P2-03 横纵手势矩阵；这些分别沿用原工作包 GUI 证据并由本轮自动回归覆盖相关逻辑。未进行真实触控和额外 DPI/硬件验证。GUI 会话包含大量暂停、交互和空闲，日志的平均 fps 不作为性能指标。

## 6. 失败历史与复验口径

| 历史工作包 | 已发生的问题与处理 | P2-05 保留方式 |
|---|---|---|
| P2-01 | 首轮真实停止/重播 1 项黑帧等待失败，补显式重绘；首轮默认 349/1/9 的新增测试过度要求控制条可见性不变，按正确契约修断言 | 不删历史；最终本轮回归重新覆盖停止/重播与失败展示 |
| P2-02 | 锁定文件测试仅预期 IOException，而本机返回产品已处理的 UnauthorizedAccessException，修正断言；长路径掩盖文件名已修显示 | 不把首次失败写成产品数据损坏；保留原文件字节/临时文件清理测试。历史物理 Esc 中断和 UIA 定位失败不是本轮程序失败 |
| P2-03 | 首轮默认 470/2/9 暴露关闭后通知；GUI 又发现预览裁剪、焦点误取消与 EOF 禁用滑块归零，逐项修正后复验 | 本轮使用最终实现；历史中间 GUI 和最终 EOF 复验身份继续区分 |
| P2-04 | 560 高度轨道内容区不足，改 640 后通过；首轮 Release 524/1/9，连续性零丢帧断言观察到 2 帧；未改代码/断言的单项及全量复验通过，根因未证实 | 本轮重新执行并保留首跑及任何复验，既有 2 帧失败不因本轮通过而消失；不得放松断言或据功能复验关闭 PERF-02 |

本轮代码审查发现启动参数慢加载可能推迟关闭的等待链：页面 `_initializationTask` 不仅等待渲染/会话初始化，还包含命令行位置参数媒体的 `OpenMediaAsync`。关闭页面先等待该初始化任务，之后才调用 ViewModel 释放，慢网络加载可能因此推迟收到释放流程的取消。已将页面初始化与启动媒体加载分开管理，并在 Diagnostics 和应用恢复脚本中新增 `close-during-startup-load` Debug 探针：真实位置参数指向 loopback 挂起 URL，要求关闭及加载结束共用 5 秒预算。0.192/0.193 秒是修复后关闭/加载结束的实际结果；未对修复前版本运行同一探针，不能作为修前修后实测对照。修复后的两配置构建/默认/显式全量、四个真实应用探针与最终 Release GUI 已通过，具体证据见第 5 节。

两配置默认及显式四轮均首次通过，本轮自动回归未发生失败重跑；最终 GUI 与身份清理通过。前序工作包首次失败及其复验仍保留，不能用本轮结果抹去历史失败或推定其未证实根因已解决。

## 7. 发布身份与未验证范围

当前发布登记仍为 `MpvShell-phase1-20261018-120914-200896`，来源为 P1-05 的 `d7522cba...` 加当时 dirty 快照，691 个包文件、257 个源码快照文件的核验均属于该次包证据。P2-05 未生成新包、未覆盖 ZIP、未更新 [唯一发布入口](../release-status.md)；本轮源码通过不代表 Phase 1 包包含停止/倍速、最近打开持久化或新界面。

本轮 `phase1-package-identity.json` 已确认发布 ZIP 的 SHA-256 为 `BAD4D64F8157F71A12E78651822378F370383475E8220B453FB26ACC68BA90D7`，对应源码 ZIP 为 `70FDABC9B0613E4FE01F6F195BBB124A65B2F9D3919C833AABF4BF4323D66BB5`，均与原登记一致。

以下范围保持独立：

- PERF-02 有采样/无采样对照；测量结果不等于 4K60 流畅度通过，不从 GUI 空闲/暂停混合日志估算播放性能。
- 真实触屏、Windows 触控注入、活动拖动中的系统触控取消；逻辑层合成位移及 48 像素控件尺寸不能替代触屏设备验收。
- 跨屏/双显示器、其他 DPI/Intel/AMD 等硬件矩阵、长期稳定性及 HDR 仪器测量；既有有限本机功能证据不扩展到这些范围。
- 播放列表、自适应码率产品策略、时间轴缩略图、外部字幕检索/下载等范围外产品功能。

## 8. 最终结论

P2-05 与 Phase 2 在声明范围内通过：慢启动加载的关闭等待链已修复，4 个真实应用生命周期探针通过；两配置构建均 0 警告/错误，默认各 525 通过、9 跳过，显式各 534 通过、无跳过，四轮均首次无失败。最终 Release 完整打开、控制、换片、停止/重播、退出/重启流程和历史清理完成；148 项构建输入及 4 个二进制哈希全部一致。

Phase 3 尚未启动。PERF-02、真实触屏、额外 DPI/硬件与长期范围继续独立跟进，本轮没有生成新发布包，Phase 1 包身份不变。
