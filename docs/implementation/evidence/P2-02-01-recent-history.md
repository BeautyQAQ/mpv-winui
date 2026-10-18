# P2-02 最近打开持久化

> 状态：声明范围内通过；实现、自动回归及真实恢复/清空重启验收完成。
> 日期：2026-10-18
> 对应计划：[P2-02 工作包](../phase-2-plan.md#p2-02最近打开持久化)
> 当前进度：[Phase 2](../phase-2-progress.md)

## 1. 范围与输入身份

本轮在 P2-01 停止/倍速已通过的工作区继续完成最近打开的用户本地保存、启动恢复及清空，沿用最新优先、精确字符串去重和 10 条上限。只在成功打开后加入记录，恢复本身不自动播放，不增加播放列表或其他协议。

开始时仍为 `main / 891d859` 上的未提交工作区；本轮 `artifacts/p2-02/source-identity.json` 记录基线提交、dirty 状态及 135 个源码/项目文件 SHA-256。继续 GUI 验收前逐项核对全部一致，未修改代码或重复构建/测试，使用最终 Release 原构建。P2-01 的 `artifacts/p2-01/source-identity.json` 及各 359 项显式回归只标识该次源码，不代替本轮源码与结果。当前发布入口仍为 [Phase 1 包](../release-status.md)，本轮不提交、不生成新包。PERF-02 与额外硬件、触屏、DPI、跨屏及长期验证保持原状态。

## 2. 验收映射

| 条件 | 本轮证据 | 当前结论 |
|---|---|---|
| 成功打开后保存，重复移动至首位，保留最多 10 条 | 存储/ViewModel 测试通过；首次 GUI 两条记录、重复打开、JSON 快照及退出通过 | 通过；最长列表只由自动测试验证 |
| 启动恢复顺序与条目，恢复不自动播放 | 新存储实例读回及 ViewModel 初始化测试通过；最终 Release 欢迎页不自动播放，最近面板按保存顺序恢复两条，点击后实际播放 | 通过 |
| 清空后重启为空，初始化/旧加载/排队保存不会复活旧记录 | 清空后新存储实例读回、初始化/加载/写入竞态测试通过；真实界面清空、正常退出、再次启动后空态与 JSON 空数组一致 | 通过 |
| 缺失、损坏、不可写文件不阻止初始化和播放 | JSON、存储及 ViewModel 失败路径测试通过 | 通过 |
| 同目录可恢复写入、关闭等待保存、失败可诊断 | 锁定目标替换失败保留旧字节/清理临时文件、关闭 drain 等测试通过 | 通过 |
| 路径/协议限制沿用打开边界，日志不泄漏查询参数或令牌 | 独立审查、过滤及诊断测试通过；新增显示转换 7 项通过；最终 GUI 文件名清晰且点击后仍使用完整路径 | 通过；网络地址显示由自动测试验证 |
| 两种配置构建及默认全量回归 | Debug/Release 构建均 0 警告/错误，默认各 411 通过、0 失败、9 跳过 | 通过；9 项显式输入场景本轮未执行 |

## 3. 当前实现与审查

生产实例在 `%LOCALAPPDATA%\MpvShell\recent-history.json` 保存版本为 1 的 JSON，仅包含 `version` 和 `items`。条目保留完整地址以便重新打开，网络地址查询参数和令牌不写入诊断日志；文件不用于远程同步。纯逻辑测试仍可使用无参的内存实例。

`RecentUrlStore` 串行执行读取、加入与清空，提供只读快照，拒绝空白、NUL、超过 32768 字符及不支持的协议/相对路径。恢复时不要求本地文件当前存在，用户点击时仍走已有后端校验。`JsonRecentUrlStorage` 在后台读写，读取上限为 4 MiB，同目录临时写入并刷新后才替换目标，失败不覆盖原文件；不自动恢复旧备份，避免清空后的记录复活。

ViewModel 的历史初始化和保存与播放解耦；保存失败单独显示在最近记录面板，不覆盖播放错误。历史版本标记隔离清空前的旧加载和迟到结果，清空不停止当前播放；关闭先释放播放资源，再等待已接受的历史任务。最近记录面板增加空态与清空入口，空列表仍可执行清空，以覆盖启动尚未恢复及前次清空写入失败后的重试。

独立审查核对了启动/加入/清空排队、旧加载、关闭等待、文件替换、输入过滤与诊断内容。审查中发现存储的初稿地址长度上限 8192 与后端 32768 不一致，且 HTTP/HTTPS 分支前未拒绝 NUL；已对齐，并将文件大小限制从 1 MiB 提至 4 MiB，以容纳 10 条最长地址的 JSON 转义。上述为代码审查及修正记录，运行结果见后续实际验证。

## 4. 验证安排及边界

本轮改动限于 App 持久化与交互，执行存储/ViewModel 定向测试、x64 Debug/Release 构建及默认全量回归。默认测试中需显式媒体或性能输入的 9 项按实际结果记为跳过；本轮不重复 P2-01 已执行的完整显式媒体、硬件和性能矩阵，若出现相关回归再扩展。

真实应用完成首次空态、两条固定本地样本打开、最近条目重新打开及排序、正常退出，并在最终 Release 原构建完成文件名显示、重启恢复、最近条目实际播放、清空及清空后再次重启。启动前默认历史文件不存在，本轮只使用新建测试记录，最终用户目录文件为 `{"version":1,"items":[]}`。

此前最终 Release 启动调用曾收到用户按物理 Esc 停止计算机操作的信号，当轮立即停止 UI 操作；该次启动结果未确认，没有记作程序启动失败。用户随后明确要求继续，才执行以下剩余 GUI 验收。GUI 范围为本机 GTX 1060、SDR、默认宽窗口及本地固定素材；截图仅保留在工具输出，文件证据为 UI 树、进程身份、JSON 快照和会话日志，不扩大到网络 GUI、窄窗、触屏或其他硬件矩阵。

## 5. 实际执行与失败历史

2026-10-18：已核对架构、P2-02 工作包验收标准、现有内存 `RecentUrlStore`、ViewModel 加载成功路径和窗口关闭顺序。存储、ViewModel/UI 与独立审查并行进行；初稿已落盘，已完成上述独立审查。

| 执行 | 实际结果 | 证据与说明 |
|---|---|---|
| 初次 Debug x64 构建 | 0 警告、0 错误 | `artifacts/p2-02/Debug-build-initial.log` |
| 首次历史定向回归 | 54 项，53 通过、1 失败 | `artifacts/p2-02/targeted-initial-tests.log`；唯一失败是新增锁定目标文件测试仅预期 `IOException`，本机文件替换返回 `UnauthorizedAccessException`。产品已处理两种异常；已修正断言，并在最终默认回归中继续验证原文件字节保持及临时文件清理。 |
| GUI 首次启动、打开及重复排序 | 实际流程通过，发现长路径显示问题 | 默认历史文件原先不存在；空态打开 `multitrack.mkv`、`video-only.mkv` 后最近面板显示两条，点击 `multitrack.mkv` 重新打开后移至首位且不重复。`gui-history-two-items.json`、`gui-history-reordered.json` 保存两次真实文件快照，素材身份见 `gui-media-inputs.json`，均位于 `artifacts/p2-02/`。正常 Alt+F4 后进程退出，资源按序释放见 `gui-session-create-reorder.log`。 |
| 最终 Debug x64 构建及默认回归 | 构建 0 警告/错误；411 通过、0 失败、9 跳过 | `artifacts/p2-02/Debug-build-final.log`、`Debug-default-final-tests.log` 及同名 TRX 目录；Abstractions 2、LibMpv 143、App 196、Rendering 70 通过，Rendering 另有 9 项需显式输入而跳过。 |
| 最终 Release x64 构建及默认回归 | 构建 0 警告/错误；411 通过、0 失败、9 跳过 | `artifacts/p2-02/Release-build-final.log`、`Release-default-final-tests.log` 及同名 TRX 目录；四项目通过/跳过数量与 Debug 相同。 |
| 历史相关最终用例 | 两种配置各 62 项全部通过 | 包含在最终默认结果中：`RecentUrlStoreTests` 21、`JsonRecentUrlStorageTests` 16、`PlayerViewModelRecentHistoryTests` 18、`RecentSourceDisplayConverterTests` 7；覆盖最大长度和锁定目标文件替换失败修正。 |
| GUI 中断历史 | 当轮未完成，收到用户停止 UI 操作信号 | 尝试启动最终 Release 时计算机操作工具报告用户物理 Esc 停止；启动结果未确认，当轮未继续 UI 操作，未记作产品失败。 |
| 最终 Release 恢复、播放及清空 | 通过 | 进程 10148 于 13:50:50 启动，欢迎页不自动播放；最近面板恢复 `multitrack.mkv`、`video-only.mkv` 顺序且文件名清晰。点击 `video-only.mkv` 后使用完整路径实际播放约 4 秒至 EOF，并移至首位。清空后显示空态，文件为 `{"version":1,"items":[]}`；13:53:35 正常关闭，渲染资源先于会话释放，进程退出。证据见下表。 |
| 清空后再次启动及正常退出 | 通过 | 进程 26352 于 13:54:12 启动，最近面板为空、无列表条目，JSON 仍为空数组；13:56:08 正常关闭并确认进程消失。证据见下表。 |

首次构建后另增加 10 条、每条 32768 字符的中文地址回读测试，用于验证本轮修正的输入长度与 JSON 文件上限；首次 54 项结果尚未包含此测试。

最终构建/默认回归统一由 `pwsh -NoProfile -File artifacts/p2-02/verify-final.ps1` 执行，脚本清除进程中的 `MPVSHELL_TEST_*` 输入，再顺序执行两配置 `dotnet build mpv-winui.slnx -c <配置> -p:Platform=x64 --no-restore` 及 `dotnet test mpv-winui.slnx -c <配置> -p:Platform=x64 --no-build --no-restore --logger trx`，脚本退出 0。

首次 GUI 操作发现历史条目直接显示完整路径并在末尾省略，同目录长路径样本的文件名被隐藏，导致两条在界面上难以区分。已补充显示转换：本地条目显示文件名并以完整路径为提示；网络条目显示主机与路径，隐藏查询参数、片段和用户信息。保存及实际打开仍使用原始完整地址；新增显示转换测试覆盖相关输入，修正后的两种配置构建及自动测试已通过。最终 GUI 恢复列表确认两条文件名可区分，点击后仍打开完整路径。

继续 GUI 验收时有一次“最近”按钮自动化点击报告 `geometry unavailable`；刷新窗口列表、状态和截图后重试成功，属于自动化定位问题，没有记作产品失败。

| 最终 GUI 证据（均位于 `artifacts/p2-02/`） | 内容 |
|---|---|
| `gui-acceptance.json` | 最终两次运行、验证范围及结论汇总；源码哈希复核一致，没有重复构建/测试 |
| `gui-restore-process.json`、`gui-restarted-process.json` | 两次运行的最终 Release 可执行路径、进程 ID 与启动时间 |
| `gui-restored-tree.txt` | 欢迎页及恢复后的两条文件名，顺序为 multitrack、video-only |
| `gui-history-after-restored-open.json` | 点击 video-only 后移至首位，两个完整路径且无重复 |
| `gui-cleared-tree.txt`、`gui-history-cleared.json` | 清空后的界面空态及版本 1 空数组 |
| `gui-restarted-empty-tree.txt`、`gui-history-restarted-empty.json` | 再次启动仍为空态，无历史条目，文件仍为空数组 |
| `gui-session-restore-clear.log`、`gui-session-restarted-empty.log` | 最终 Release 初始化、实际播放/EOF、两次正常关闭及资源释放顺序 |

P2-02 在上述声明范围内通过。下一工作包为 P2-03 时间轴数字预览与手势反馈，尚未开始；Phase 2 整体仍在进行中。本轮没有提交或生成新发布包。
