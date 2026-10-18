# P2-04 播放器界面完善

> 状态：本机功能验收通过；最终构建、默认回归/复验与声明范围内真实键鼠/窗口验收完成。
> 日期：2026-10-18
> 对应计划：[P2-04 工作包](../phase-2-plan.md#p2-04播放器界面完善)
> 当前进度：[Phase 2](../phase-2-progress.md)

## 1. 范围与输入身份

在 P2-01～P2-03 的未提交工作区上完善播放器界面，基线提交仍为 `main / 891d8597e5df57e8d3376618f4f6c2c7412da241`。沿用既有 libmpv、ANGLE/D3D11、状态与生命周期边界；本包补齐控制条、状态反馈、音轨/字幕面板、窗口、焦点及自动隐藏体验，不新增播放列表、外部字幕检索/下载或时间轴缩略图。

本轮原始日志、TRX、合成媒体、源码身份与 GUI 文件证据放在 `artifacts/p2-04/`。当前对外包仍为 [Phase 1 验收包](../release-status.md)，本轮没有生成新发布包。P2-03 两配置默认各 478 通过、9 跳过及其 140 个源码哈希仅属于上一包，不能代替本轮回归。

执行时的 `source-identity.json` 保留 147 个源码/项目/解决方案文件哈希；15:43:12 的 `closure-verification.json` 确认其中 146 项完全一致，唯一差异为 `mpv-winui.slnx` 新增 P2-04 文档 `File` 索引，项目定义及编译源未变。原 manifest 未覆盖，收尾记录单列新索引哈希。最终 Release EXE/DLL 最后写入均为 15:16:40，早于 GUI 进程 12860 的 15:18:46 启动，二进制 SHA-256 亦保存在收尾记录中；最终 GUI 期间没有修改源码或重新构建。

## 2. 已实现的交互契约

- 控制条以窗口宽度 1100 为单行/双行切换阈值，主要按钮、音量滑块和时间轴具有至少 48 像素的操作高度。媒体标题省略并提供完整提示；时间轴预览保留独立布局行。
- 最小窗口下限由 `OverlappedPresenter.PreferredMinimumWidth/Height` 设置为 720×640，切换 presenter 后重新应用。初版高度 560 的可用性缺陷及修正见第 3 节；此处不据 API 名称推断额外 DPI 矩阵已通过。
- 音轨、字幕分组显示，字幕包含关闭项；长轨道名称换行/省略并提供完整提示。选择标记依据后端事实更新，命令提交不提前宣称成功；相同轨道对象原位更新，位置事件不重建列表或移动面板焦点。
- 加载、缓冲、EOF、停止和错误使用单一状态提示，避免叠加遮挡。浮层打开时隐藏状态卡，关闭后保留原状态；从浮层打开媒体先关闭浮层，使加载/错误提示可见。
- 停止及 EOF 后重播进入加载态，等待后端确认；停止可以取消等待中的重播，迟到成功/失败和旧 EOF 不能覆盖新的加载状态。
- `PlayerChromePolicy` 只在播放中、窗口活动、无浮层/活动操作/预览/瞬时界面、鼠标离开控制区且没有控件键盘焦点时允许闲置隐藏。UI 桥接另将加载、缓冲、文件选择器及倍速菜单计入阻挡；触控不遗留鼠标悬停状态。
- Esc 每次只处理一层，顺序为输入/预览、瞬时界面、浮层、全屏；原生菜单/选择器处理自己的关闭。浮层关闭恢复原控件及键盘焦点状态。F11 可从地址框切换全屏，文本编辑及 Alt+Space 等系统组合键不会误触播放。

## 3. 执行与修正记录

| 执行 | 实际结果 | 证据 / 说明 |
|---|---|---|
| 首次 Debug 构建与 App 回归 | 构建完成；App 281 通过、0 失败、0 跳过 | `Debug-build-initial.log`、`targeted-initial-tests.log` 及 `targeted-initial-tests/` TRX；属于初版源码 |
| 表示层定向回归 | 199 通过、0 失败、0 跳过 | `presentation-focused/presentation-focused.trx`；包含状态卡、轨道更新与重播相关场景，最终数量以最终源码全量结果为准 |
| 初版接线审查 | 发现并修正自动隐藏与焦点缺陷 | 文件选择器阻挡标识曾误放在输出刷新方法，导致初始化后一直阻挡自动隐藏；已移回文件选择器生命周期。关闭浮层曾固定恢复 Programmatic 焦点，已保存/恢复原 Keyboard 状态，避免 Tab 返回控件后被隐藏 |
| 初版 Release 构建及真实窗口 | 发现最小高度不足，当时未判定 GUI 通过 | `Release-build-before-gui.log`；进程 22704，15:07:09 启动、15:15:35 正常关闭；初版 720×560 下轨道内容区过矮，后续改为 720×640，修正后结果见第 5 节 |
| 初版窗口操作过程 | 保留无效操作，不计为通过 | 尝试拖动窗口边缘时误落到画面并触发 seek，此操作不能作为窗口尺寸或手势冲突验收依据；后续按明确窗口边框/系统操作复验 |
| 最终 Debug/Release 构建 | 两配置均 0 警告、0 错误 | `Debug-build-final.log`、`Release-build-final.log` |
| 最终 Debug 默认全量 | 525 通过、0 失败、9 跳过 | `Debug-default-final-tests.log` 与同名 TRX 目录；App 297、Abstractions 2、LibMpv 156、Rendering 70 通过，Rendering 另 9 项条件跳过 |
| 首轮最终 Release 默认全量 | 524 通过、1 失败、9 跳过 | `Release-default-final-tests.log` 与同名 TRX 目录。既有 `RenderContinuityIntegrationTests` 零丢帧断言实际观察到 2 帧，首次失帧根因尚未证实；保留失败，不能写成首次全量通过 |
| Release 渲染连续性单项复验 | 1 通过、0 失败、0 跳过 | `Release-render-continuity-recheck` 结果；未修改代码或原断言，同一二进制复验。单项通过不抹去首次失败，也不代替全量复验 |
| 同一 Release 二进制默认全量复验 | 525 通过、0 失败、9 跳过 | `Release-default-recheck-tests/` 的 4 份 TRX 及对应日志；App 297、Abstractions 2、LibMpv 156、Rendering 70 通过，Rendering 另 9 项条件跳过。保留首次失败，不放宽原断言，不将本次功能复验写成性能矩阵通过 |
| 修正后最终 Release 主体 GUI | 最小窗口、轨道/字幕、全屏/Esc、EOF/重播已完成复验 | 进程 12860；720×640 布局完整，轨道面板可滚到底，选择第二音轨、关闭/重新选择长标题字幕后勾选正确且面板保持打开；首次 Esc 关闭面板并保持全屏，再次 Esc 退出；EOF 正确，重播加载态与实际重播已确认 |
| 最终焦点/自动隐藏与退出清理 | 通过 | 播放中鼠标悬停与键盘焦点均保持控件可见；Esc 关闭最近面板恢复键盘焦点，至少 12 秒后仍显示，点击画面后闲置隐藏。Space 暂停恢复、停止归零和本地输入校验错误卡通过；15:42:02 正常退出，进程无残留，历史文件字节恢复且哈希相等 |

最终自动验证脚本 `pwsh -NoProfile -File artifacts/p2-04/verify-final.ps1` 清除进程中的 `MPVSHELL_TEST_*` 输入，再对 Debug、Release 顺序执行：

```powershell
dotnet build mpv-winui.slnx -c <配置> -p:Platform=x64 --no-restore --verbosity minimal
dotnet test mpv-winui.slnx -c <配置> -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p2-04/<配置>-default-final-tests --verbosity minimal
```

需显式媒体或性能输入的条件测试按照实际跳过记录，不记作通过。源码 manifest 与文档索引的最终核对分别记录；中途构建与最终 GUI 不得仅凭相同产物路径视为同一身份。

`closure-verification.json` 逐份复核四轮 TRX。汇总 `Counters.notExecuted` 未正确列出条件跳过，因此以每个 `UnitTestResult outcome="NotExecuted"` 计数，与控制台每次默认全量 9 项跳过一致。

## 4. 验收映射

| 条件 | 自动回归 / 实现证据 | 当前 GUI 状态 |
|---|---|---|
| 完整控制入口、合理禁用与宽窄布局 | PlayerPage 提供播放/暂停、停止、倍速、seek、音量/静音、轨道/信息/最近及全屏入口；ViewModel 输出可操作状态与禁用原因 | 最终 720×640 布局完整；长标题保留提示，宽/窄及全屏往返已验证；初版 560 高度失败保留 |
| 单一状态提示及重播取消 | `PlayerViewModelPresentationTests` 覆盖加载/缓冲/错误优先、旧 EOF、停止/EOF 重播、停止取消等待及旧结果隔离 | 最终 EOF 显示“播放结束”，禁用时间轴保留 120.021 秒与 `02:00 / 02:00`；重播加载态、成功重播、停止归零及单一输入校验错误卡已验证。缓冲、后端失败竞争及慢加载取消依据自动回归 |
| 轨道分组、关闭字幕、长名称与后端实际选择 | `PlayerViewModelPresentationTests`、`PlayerViewModelTrackSelectionTests` 覆盖分组、关闭项、后端选择事件及原位更新 | 最终选中第二音轨、关闭字幕、再次选择长标题字幕均显示正确勾选；面板保持打开，最小窗滚动可到达字幕列表 |
| 鼠标、键盘、浮层及活动操作阻挡隐藏 | `PlayerChromePolicyTests` 与 UI 事件桥接，计时器启动和触发时均重新评估条件 | 悬停时播放位置 52.8→79.2 秒且控件可见；0.25× 播放中最近按钮键盘焦点恢复后，移开鼠标，位置 53.7→56.8 秒仍保留焦点轮廓及控件；点击画面后闲置隐藏 |
| Esc、焦点恢复、F11 与系统快捷键边界 | Policy 验证层次优先级；PreviewKeyDown 在编辑控件 guard 前处理 Esc/F11，浮层恢复焦点类型，组合键保护避免误播放 | F11 进入全屏，首次 Esc 关面板仍显示“退出全屏”，再次 Esc 恢复窗口；最近面板 Esc 关闭后恢复键盘焦点并保持至少 12 秒，Space 暂停恢复已验证。系统组合键保护属于代码桥接审查 |
| 最终源码与构建/默认回归一致 | 两配置构建与 Debug 默认/Release 全量复验完成，源码身份见 `source-identity.json`，收尾核对见 `closure-verification.json` | GUI 使用最终 Release，期间未改源码；初版构建/窗口失败与最终验收分别登记 |

## 5. GUI 输入与证据

本轮合成媒体为 `播放器界面验收-很长的媒体标题用于检查最小窗口与全屏切换时的省略显示-双音轨与内嵌字幕-120s.mkv`，位于 `artifacts/p2-04/`。它使用 P2-03 的 120 秒、320×180、10 fps 合成视频，加入两条 AAC 音轨和一条内嵌字幕，使用长文件名/音轨名/字幕名。媒体 SHA-256 为 `200A83D07E25E80DB3CA3E5D26BF21F82AF66045E094776D61D48CDF201EA2EE`，生成参数与 ffmpeg 身份见 `media-manifest.json`。该输入仅用于交互功能，不用于性能判断。

初版 Release 进程 22704 的会话日志已复制到 `gui-session-initial.log`，原始日志为 `%LOCALAPPDATA%\MpvShell\logs\session-20261018-150709-335-22704.log`。日志记录 GTX 1060 5GB、ANGLE/D3D11、SDR 输出，正常关闭时按 render context、EGL/SwapChain/D3D11、mpv core 顺序释放，并记录主窗口关闭。

| 初版证据 | 含义与限制 |
|---|---|
| `gui-initial-wide-tree.txt` | 初版宽窗口 UI 快照；不是最终构建验收 |
| `gui-initial-minimum-tree.txt` | 初版窗口操作快照；必须结合可见状态判断，不单独证明内容可用 |
| `gui-initial-560-height-track-tree.txt` | 初版 560 高度轨道面板现场，保留可视区过矮的失败历史 |
| `history-before-gui.json` | 验收前最近打开文件备份；最终 GUI 后已字节恢复并核对哈希 |

最终 Release 进程为 12860，15:18:46 启动，路径与身份见 `gui-final-process.json`；`gui-acceptance.json` 汇总实际操作、结果及未验证边界。已完成的最终 GUI 证据如下；截图以计算机操作工具实际输出为准，UI 树提供对应控件与状态快照。

| 最终操作与结果 | 文件证据（均在 `artifacts/p2-04/`） |
|---|---|
| 720×640 窗口布局完整，长标题省略而控制入口可用 | `gui-final-minimum-tree.txt` 及操作截图；尺寸依据实际窗口操作确认，UI 树本身不包含窗口几何 |
| 选择第二条音轨和关闭字幕，勾选随后端更新，面板保持打开 | `gui-final-track-selection-tree.txt` |
| 最小窗口轨道面板滚动到底可访问字幕，重新选择长标题字幕后恢复勾选 | `gui-final-minimum-tracks-scrolled-tree.txt`、`gui-final-minimum-subtitles-tree.txt`；后者滚动值为 191，第二音轨及内嵌字幕均标记已选择 |
| 全屏中第一次 Esc 仅关面板，仍显示“退出全屏”；第二次 Esc 恢复窗口 | `gui-final-fullscreen-first-escape-tree.txt` 与随后操作截图 |
| EOF 显示结束提示、禁用定位且保留末尾进度 | `gui-final-eof-tree.txt`，滑块 120.021，`02:00 / 02:00` |
| 重播进入加载态，定位/播放禁用而停止仍可用，随后实际重播成功 | `gui-final-replay-loading-tree.txt` 与随后操作状态；加载快照显示“正在打开媒体”和忙碌指示 |
| 鼠标停在控制区期间，播放位置从 52.8 到 79.2 秒，控制区仍可见 | `gui-final-pointer-hover-tree.txt` 与操作截图 |
| 0.25× 活跃播放中，Esc 关闭最近面板恢复最近按钮键盘焦点，鼠标移到视频区域；位置 53.7→56.8 秒后，至少 12 秒仍保持焦点轮廓及控制区 | `gui-final-keyboard-focus-tree.txt` 与操作截图；期间播放仍推进，不以暂停状态充当隐藏阻挡验证 |
| 点击视频将焦点移出控件后，闲置隐藏全部控制区；Space 可暂停并恢复界面 | `gui-final-auto-hidden-tree.txt` 只保留窗口/画面与系统标题栏，焦点为画面窗格；Space 完成态见随后操作 |
| 停止显示“已停止”，时间和滑块归零，seek 和停止入口禁用 | `gui-final-stopped-tree.txt`，滑块 0，`00:00 / 00:00` |
| 输入确认不存在的本地 `missing-ui-verification.mkv`，仅显示“播放遇到问题”错误卡 | `gui-final-error-tree.txt`；这是输入校验失败，不是后端网络失败测试 |
| Alt+F4 正常退出，依次释放渲染上下文、EGL/SwapChain/D3D11 与 mpv core | `gui-session-final.log`，15:42:02 主窗口关闭；`gui-cleanup.json` 确认进程已退出 |

最终进程 12860 于 15:42:02 正常退出并确认不存在。验收后历史文件按备份字节恢复，SHA-256 为 `024E43BF323BD590229148F46E831F64EB114AAC28F44E8F6C9CA7C0932A408F`，与验收前一致；`gui-cleanup.json` 记录检查时间与恢复结果，没有留下本轮测试条目。

文档入口检查确认 6 个文本为严格 UTF-8 无 BOM、82 个本地链接目标存在；`dotnet sln mpv-winui.slnx list` 列出 8 个项目，`git diff --check` 无空白错误。

## 6. 当前结论与边界

P2-04 在上述本机功能范围内通过：最终两配置构建、Debug 默认与 Release 全量复验通过；首轮 Release 的渲染连续性测试出现零丢帧断言失败，原代码/断言单项和同一二进制全量复验随后通过，首次失败及未证实根因保留。最小高度修正后的窗口、轨道/字幕、全屏/Esc、EOF/重播、焦点/自动隐藏、停止、输入校验错误及正常退出均完成真实应用验证，历史文件已恢复。P2-05 尚未开始，Phase 2 整体未完成。

48 像素控件尺寸与策略/协调层输入测试不能替代物理触屏验证。真实触屏、额外 DPI/显示器、跨屏、其他 GPU、长期稳定性及 HDR 仪器测量继续保持未验证，PERF-02 独立跟进。当前 Phase 1 包身份不变，本轮不生成新发布包。
