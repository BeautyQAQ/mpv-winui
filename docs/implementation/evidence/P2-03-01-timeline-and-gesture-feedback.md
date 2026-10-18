# P2-03 时间轴数字预览与手势反馈

> 状态：声明范围内通过；实现、自动回归及真实键鼠/窗口交互验收完成。
> 日期：2026-10-18
> 对应计划：[P2-03 工作包](../phase-2-plan.md#p2-03时间轴预览与手势反馈)
> 当前进度：[Phase 2](../phase-2-progress.md)

## 1. 范围与输入身份

在 P2-01 停止/倍速和 P2-02 最近打开持久化的未提交工作区上继续实现数字时间预览、鼠标/键盘/合成手势目标、取消和操作反馈。基线提交仍为 `main / 891d8597e5df57e8d3376618f4f6c2c7412da241`，工作区含前两个工作包的未提交改动。当前对外包仍为 [Phase 1 验收包](../release-status.md)，本轮不提交、不重新发布。

`artifacts/p2-03/` 保存本轮源码身份、构建日志、TRX、媒体与 GUI 证据。`source-identity-before-gui-fixes.json` 对应 GUI 裁剪/焦点修正前的自动回归；`source-identity-before-eof-display-fix.json` 对应主体交互 GUI 验证使用的 Release 源码。两份快照与最终 `source-identity.json` 分开登记，不能因产物路径相同而视为同一构建。最终 manifest 于 14:49:54 登记，140 个源码/项目文件哈希逐项一致，对应最终两配置构建、默认回归及 EOF/停止复验。

不包含时间轴缩略图、播放列表或 P2-04 的完整界面重做。PERF-02、真实触屏、跨屏、其他硬件/DPI 和长期稳定性保持独立；逻辑层合成触控输入不能替代触屏设备验收。

## 2. 契约与验证安排

- 后端根据原生 `seekable` bool 提供定位能力，UI 同时要求有限正时长和可操作媒体。普通 EOF 卸载后不可 seek；原生 keep-open 保留媒体时沿用实际能力，不增加“自动重载再定位”语义。禁用操作与显示实际进度分离：有限正时长的不可 seek 媒体仍显示实际位置，EOF 应显示满进度，停止后显示零进度。
- 时间轴悬停只显示目标时间；拖动、键盘待提交目标与媒体真实位置分离，迟到位置事件不能覆盖当前预览。
- 正常松手只提交一次；取消捕获、Esc、失焦、停止、换片、故障及关闭清除待提交输入和反馈。已提交的旧回复不能复活旧预览或覆盖新操作反馈。
- 键盘时间轴输入合并 180 ms，音量输入合并 100 ms；画面手势超过 40 px 后锁定方向，横向 8 px/秒，纵向 4 px/音量百分点，松手提交。
- 成功或失败反馈保留 1.5 秒后消失，取消即清除；浮层优先，正在交互时不自动隐藏控件；XAML code-behind 只负责输入、几何和生命周期桥接，竞争策略由可测试协调层负责。

执行协调层、ViewModel、原生会话定向回归，以及 x64 Debug/Release 构建和默认全量回归。新增可 seek 原生测试使用本地生成 WAV 或进程内 HTTP 测试服务，不依赖外部站点。真实 GUI 使用 `artifacts/p2-03/timeline-120s.mp4`（120 秒、320×180、10 fps、无音轨的合成样片），SHA-256 为 `60157738F6063D36931DA14AD629F5211CB9934BA0D570B68901620E58612805`；生成命令及工具身份见同目录 `media-manifest.json`。它仅用于交互功能，不用于性能结论。

## 3. 执行记录

开始前应用未运行，用户目录历史文件为空数组，已备份至 `artifacts/p2-03/history-before-gui.json`。实现、审查与测试结果继续登记于此，不把尚未执行的验收写成通过。

初稿审查修正了四个边界：指针捕获丢失不能视为提交；仅悬停不能移动滑块；实际位置高频更新不能反复重置自动隐藏计时器；外部取消令牌结束请求后不能遗留“正在跳转”反馈。后端补读 `seekable`，处理 mpv 观察值合并，并由媒体代次及属性版本保护查询结果。

| 执行 | 实际结果 | 证据 / 说明 |
|---|---|---|
| 首次 Debug 构建 | 0 警告、0 错误 | `artifacts/p2-03/Debug-build-initial.log` |
| 首轮定向回归 | 112 通过、0 失败、0 跳过 | `targeted-initial-tests.log` 与同名 TRX 目录；App 76、LibMpv 35、Abstractions 1；渲染项目无匹配用例 |
| 首轮 Debug 默认全量 | 470 通过、2 失败、9 跳过 | `Debug-default-initial-tests.log` 与同名 TRX 目录。两个失败均为既有 `Dispose_should_release_backend_then_wait_for_history_without_updating_closed_ui` 的参数用例：新交互清理在关闭后仍发属性通知，违反不更新已关闭界面的契约。保留原断言，修复产品通知路径后再复验。Release 当时尚未执行。 |
| 关闭通知修正后的 Debug/Release 构建与默认全量 | 两配置均 0 警告、0 错误；各 476 通过、0 失败、9 跳过 | `Debug-build-before-gui-fixes.log`、`Release-build-before-gui-fixes.log`、两配置 `*-default-before-gui-fixes-tests.log` 及同名 TRX 目录。新增 4 个关闭后迟到成功/失败用例；Abstractions 2、LibMpv 156、App 248、Rendering 70 通过，Rendering 另 9 项条件跳过。此结果不包含后续 GUI 修正。 |
| 首轮真实 GUI 与输入诊断 | 发现预览裁剪和焦点取消缺陷，未据此判定通过 | 初始预览使用负 margin，被父布局裁剪。另一次时间轴/画面按下后，主动 Focus 使旧控件的 LostFocus 冒泡，误清除刚建立的指针操作。`gui-session-initial.log`、`gui-session-input-diagnostic.log` 保留现场；诊断日志中两种输入均出现 press 后立即失焦取消。 |
| GUI 修正后 Release 构建与主体交互 | 构建 0 警告、0 错误；以下主体交互实际通过 | 预览改为真实 24 px 布局行，焦点归属区分主动转移与真正离开输入目标；临时输入日志已移除。构建见 `Release-build-interactions-gui.log`，源码见 `source-identity-before-eof-display-fix.json`，GUI 明细见第 5 节。 |
| 主体 GUI 中的 EOF 检查 | 发现禁用滑块显示归零，已修正并复验 | `gui-final-eof-before-display-fix-tree.txt` 显示时间文本 `02:00 / 02:00`，但禁用滑块值为 0。ViewModel 已分离 `HasKnownDuration` 与 `CanSeek`，保留实际进度显示，并新增 2 个不可 seek/EOF 显示用例；最终 GUI 结果见下行。 |
| 最终 Debug/Release 构建与默认全量 | 两配置均 0 警告、0 错误；各 478 通过、0 失败、9 跳过 | `Debug-build-final.log`、`Release-build-final.log`、两配置 `*-default-final-tests.log` 及同名 TRX 目录；各 4 份 TRX 汇总：Abstractions 2、LibMpv 156、App 250、Rendering 70 通过，Rendering 另 9 项条件跳过。包括 GUI 修正和最终 2 项 EOF/不可 seek 进度显示回归。 |
| 最终 Release EOF、停止及退出复验 | 通过 | 最终进程 14548：普通 EOF 后禁用滑块值为 120，文本 `02:00 / 02:00`；停止后禁用滑块值为 0，文本 `00:00 / 00:00`。正常关闭后确认无残留进程；`gui-final-eof-tree.txt`、`gui-final-stopped-tree.txt`、`gui-eof-stop-process.json` 和 `gui-session-eof-stop-final.log` 保留证据。 |

最终构建/默认回归由 `pwsh -NoProfile -File artifacts/p2-03/verify-final.ps1` 顺序执行并成功结束。脚本清除进程中的 `MPVSHELL_TEST_*` 输入，再对 Debug、Release 分别执行 `dotnet build mpv-winui.slnx -c <配置> -p:Platform=x64 --no-restore` 和 `dotnet test mpv-winui.slnx -c <配置> -p:Platform=x64 --no-build --no-restore --logger trx`。9 项需显式媒体或性能输入的测试本轮跳过，不计作通过；P2-01/Phase 1 的显式素材结果不代替本轮执行。

## 4. 验收映射

| 条件 | 自动回归 / 代码证据 | GUI 与当前结论 |
|---|---|---|
| 悬停、拖动和真实位置分离 | `PlaybackInteractionPreviewTests`、`PlayerViewModelInteractionTests` 覆盖悬停不移动滑块、拖动期间旧位置事件不覆盖目标、松手只提交最终值 | 主体 GUI 已验证悬停 `00:29` 时滑块与实际文本保持约 90.7 秒，点击/拖动落点正确 |
| 鼠标、键盘及合成手势目标一致，越界夹取 | 协调层对相同目标执行 scrub、键盘队列与 delta 手势；覆盖负目标、超时长、40 px 阈值及锁轴 | 主体 GUI 已验证时间轴点击/拖动、Slider 方向键及画面横纵向鼠标拖动；精确边界由自动回归验证 |
| 未知时长及不可 seek 媒体禁用 | 有限正时长独立校验；原生能力默认 false。`BackendSeekabilityTests` 13 项覆盖观察/FileLoaded 时序、false/null、换片、失败、终态、EOF 和观察值合并补读；既有本地 WAV/Stop 测试增补能力及停止屏障断言 | 初始无媒体及普通 EOF 禁用可见；最终 GUI 确认 EOF 保留满进度显示。未知时长由自动回归覆盖 |
| 取消、换片、停止、失焦及时清理，旧回复不能复活预览 | ViewModel 生命周期用例覆盖取消、停止、同地址重载、浮层、能力丢失及故障；外部取消与新旧请求回复通过版本保护；关闭后的迟到成功/失败不再通知 UI | Esc 清悬停、最小化/恢复无残留及最终停止清零已验证。拖动中的强制失捕获/触控取消未单独在 GUI 注入，依据协调层自动回归及事件桥接审查 |
| 节流、阈值、方向冲突和浮层优先 | 可控 `TimeProvider` 验证 seek 180 ms、音量 100 ms，未到期不提交、到期只提交一次；阈值、锁轴及三种浮层阻挡由协调层覆盖 | 真实横向 −160 px 从 95 秒跳到 75 秒；纵向 +120 px 从音量 100 到 70 |
| 反馈显示、按时消失及自动隐藏 | 自动回归验证只接纳最新请求反馈、成功/失败 1.5 秒到期、取消立即清除，以及悬停/交互阻止自动隐藏 | 主体 GUI 已看到跳转/音量反馈及随后消失；画面持有焦点后控件自动隐藏 |
| 构建、全量回归和最终源码一致 | 首轮缺陷与修正结果完整保留；最终两配置 0 警告/错误，各 478 通过、0 失败、9 跳过 | 构建与默认全量通过；最终 140 个源码/项目文件哈希逐项一致，不以中间构建替代 |

锁定原生语义依据位于 `artifacts/p1-02-attribution/mpv-full/`：`DOCS/man/input.rst` 的 `seekable` / `partially-seekable`，`player/command.c` 的 `mp_property_seekable`，以及 `player/client.c` 的相同观察值合并逻辑。属性为 bool，无 demuxer 时 unavailable；部分缓存可 seek 不保证所有目标均可到达，失败仍由现有命令错误路径反馈。

## 5. 真实 GUI 证据

主体交互使用 Release 进程 23008，2026-10-18 14:34:44 启动，14:42:18 正常关闭；可执行路径与进程身份见 `gui-final-process.json`，完整会话见 `gui-session-interactions.log`。虽然文件名含 `final`，这批证据对应 EOF 显示修正前的源码；最终 EOF/Stop 复验单独登记。

环境为本机 Windows 10.0.26300、.NET 10.0.13、x64、NVIDIA GeForce GTX 1060 5GB、ANGLE/D3D11、SDR，默认宽窗口视频表面为 1104×721。截图保留在计算机操作工具输出；持久化文件证据为 UI 树、进程身份、源码身份、媒体 manifest 和会话日志。

| 操作与结果 | 文件证据（均在 `artifacts/p2-03/`） |
|---|---|
| 时间轴点击后约 60.7358 秒，显示“跳转至 01:00” | `gui-final-timeline-click-tree.txt` |
| 拖动松手后约 90.7 秒，显示“跳转至 01:30” | `gui-final-timeline-drag-tree.txt` |
| 悬停预览 `00:29`，实际仍为 90.7 秒 / `01:30` | `gui-final-hover-tree.txt` |
| Esc 清除预览，实际位置保持 | `gui-final-escape-tree.txt` |
| Slider 获焦后 Right 输入，落点为 95 秒 / `01:35` | `gui-final-keyboard-tree.txt` |
| 画面水平 −160 px 后为 75 秒；纵向 +120 px 后音量为 70，反馈随后消失 | 操作截图与工具状态；完成态共同见 `gui-final-feedback-expired-tree.txt`。`gui-final-horizontal-gesture-tree.txt` 和 `gui-final-vertical-gesture-tree.txt` 为异步命令刚发出时的快照，仍分别含旧位置/旧音量，不单独用作完成态证据 |
| 最小化后恢复，没有残留预览或反馈，位置 75 秒、音量 70 | `gui-final-reactivated-tree.txt` |
| 画面持有焦点并闲置后控件隐藏 | `gui-final-auto-hidden-tree.txt` |
| 正常退出，render context 先于 mpv core 释放，主窗口关闭 | `gui-session-interactions.log` |

最终 EOF/Stop 复验使用最终 Release 进程 14548，14:44:14 启动，14:46:31 正常关闭。`gui-final-eof-tree.txt` 确认不可 seek 的普通 EOF 仍显示滑块 120 和 `02:00 / 02:00`，`gui-final-stopped-tree.txt` 确认停止后滑块与时长归零；两者均无残留预览/反馈，seek 入口禁用。`gui-session-eof-stop-final.log` 记录实际加载、EOF、render context → 渲染资源 → mpv core 的释放顺序及主窗口关闭，另已确认进程退出。

GUI 前历史文件为空数组。验收后 `history-after-gui.json` 仅含本轮合成样片，完成后已恢复 `history-before-gui.json` 的原始空数组，未留下测试最近记录。

`gui-acceptance.json` 汇总各进程、对应源码快照、实际操作和清理结果；`final-verification.json` 记录最终 140 个源码哈希、两配置 TRX 逐项汇总、47 个变更文本的 UTF-8 无 BOM、75 个本地文档链接、8 项目解决方案解析及 `git diff --check` 复核结果。TRX 的汇总 `notExecuted` 字段未计入跳过项，因此以各 `UnitTestResult outcome="NotExecuted"` 计数，与控制台的每配置 9 项跳过一致。

## 6. 结论与范围

P2-03 在上述声明范围内通过：最终两配置构建及默认回归无失败，主体键鼠交互、反馈、失焦恢复、自动隐藏及最终 EOF/停止显示完成真实应用验证；失败历史与验证边界保留。下一工作包为 P2-04 播放器界面完善，尚未开始；Phase 2 整体仍在进行中。

本轮“合成手势/触控输入”指对协调层和 ViewModel 提供可控 delta 与生命周期序列，不是 Windows 触控注入，也不是物理触屏测试。真实 GUI 覆盖鼠标、键盘及窗口操作；拖动中的强制失捕获/系统触控取消未单独注入。额外窄窗、DPI、跨屏、其他 GPU、长期稳定性与 PERF-02 均未验证或独立跟进，不因本包功能回归通过而关闭。未生成新发布包。
