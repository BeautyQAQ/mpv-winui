# Phase 1 实施进度

> 总体状态：通过；Phase 1 于 2026-10-18 在计划声明范围内完成。
> 最近完成：P1-00～P1-05 已通过；PERF-01、MEDIA-01、LIFE-01 已关闭。
> 后续：Phase 2 产品交互工作另行规划；PERF-02 采样对照与既有硬件/长期验证继续独立跟进。
> 最后更新：2026-10-18
> 执行计划：[Phase 1 计划](phase-1-plan.md)
> 当前发布包：[发布状态](release-status.md)

## 1. 当前结论

Phase 0 已在声明范围内通过，正式路线和基础实现继续沿用。Phase 1 的目标是生产级播放基础：固化现有模块、控制与状态契约、资源所有权、生命周期、媒体覆盖和发布流程。完整 UI、倍速交互、最近打开持久化等产品工作不自动进入本阶段。

本轮已建立六个工作包，P1-00 文档、基线与产物入口核对通过。P1-01 的固定素材、场景、判定规则和测量工具已进入仓库；2026-09-22 在远程连接的 SDR 开发机上完成 Debug/Release 构建，两种配置各通过 14 项统计测试和 1 项测量链路自检。2026-10-02 复核用户带回的 9 月 22 日 RTX 3070 日志：4K 全屏 SDR/PQ 短时连续段约 60 fps，窗口 HDR→SDR 约 60 fps，正常 EOF 与退出；开头丢帧与混合统计段单独保留。2026-10-04 用户确认 GTX 1060 的 HDR→SDR 窗口和全屏此前复测正常，P1-01 / PERF-01 据此关闭；PERF-02 的采样对照仍保留为独立跟进项。2026-10-13 开始 P1-02，已复现并修复加载尚未提交时提前切换当前地址、屏蔽原媒体加载完成事件的问题，验证见 [加载取消边界记录](evidence/P1-02-01-load-cancellation-boundary.md)。同日继续修复后端事件积压丢失关键状态，以及播放列表 redirect 提前结束加载的问题，详见 [事件与 redirect 记录](evidence/P1-02-02-event-overflow-and-redirect.md)。随后补齐错误分类、非渲染终态保护及 UI 有界批量派发，并修复错误后内核仍播放的问题，详见 [错误与 UI 派发记录](evidence/P1-02-03-error-contract-and-ui-dispatch.md)。本轮继续修复 seek 回复竞争，补充请求取消/超时及真实跨加载边界回归，见 [seek 与请求契约记录](evidence/P1-02-04-seek-and-request-contract.md)。2026-10-16 完成收尾：对原生内部异步重排做跨加载归属审计，以 v0.41.0 源码核对、真实会话探针与托管注入确认等待期旧 StartFile 的守卫缺口，并以原生 `playlist_entry_id` 完成归属修复，见 [跨加载归属记录](evidence/P1-02-05-cross-load-attribution.md)；按计划条目完成 [P1-02 验收汇总](evidence/P1-02-06-p1-02-acceptance.md)，P1-02 标记通过。同日接续 P1-03：固化 UI、命令/事件、渲染三执行域及共享 core 的所有权与释放顺序，新增页面卸载挂起/重挂载恢复租约，并以真实 Frame 导航验证播放与暂停状态的页面重建，补齐恢复中关闭、加载中关闭、恢复中换媒体场景与关闭后资源释放核对；[所有权记录](evidence/P1-03-01-lifecycle-ownership-and-release-order.md)、[场景记录](evidence/P1-03-02-app-lifecycle-scenarios.md)与[验收汇总](evidence/P1-03-03-p1-03-acceptance.md)确认各验收条目满足，P1-03 标记通过，P1-04～P1-05 未开始。

2026-10-18 完成 P1-04：新增固定视频 HLS、多音轨、SRT/ASS 字幕样本及清单，以真实 Render API / ANGLE / D3D11 像素验证视频变化、暂停、跨分片 seek、字幕切换/关闭、404 后重载及受控缓冲恢复；修复视频仍有效时关闭音轨却保留旧编码的问题。换媒体信息清理、EOF 保留和日志脱敏通过；当时 Debug/Release 全量回归各 302 通过、0 失败、5 项既有素材/性能测试跳过。[P1-04 验收记录](evidence/P1-04-01-video-hls-subtitles-and-media-information.md)确认声明范围内通过，MEDIA-01 关闭。

同日完成 [P1-05 发布验收](evidence/P1-05-01-release-and-phase-1-acceptance.md)：最终 Debug/Release 构建各 0 警告/错误，默认回归各 298 通过、9 跳过，显式素材回归各 307 通过、0 跳过、0 失败；完成源码快照、原生闭包和原生/托管许可核验，从最终 Release 目录验证多轨与 4K HDR→SDR 样片 EOF/退出，691 个 ZIP 文件全部匹配。Phase 1 在声明范围内完成。RDP 环境的 4K 离屏测量约 10.67 fps，属于“已测量、未判定”，不关闭 PERF-02 或泛化性能结论。

## 2. 当前基线与发布身份

| 项目 | 当前记录 | 证据 |
|---|---|---|
| 已验收阶段 | Phase 0 范围内通过；旧路线已移除 | [P0-11 记录](evidence/P0-11-01-phase-0-acceptance.md) |
| 阶段起始源码基线 | `f793d0b`，2026-09-12 | [P1-00 基线记录](evidence/P1-00-01-baseline-and-release-entry.md) |
| 起始 Debug | 构建 0 警告/错误；显式素材回归 188 通过、0 失败、0 跳过 | 同上；`artifacts/progress-audit-20260912/` |
| 起始 Release | 构建 0 警告/错误；日常配置 184 通过、0 失败、4 项素材测试跳过 | 同上；`artifacts/progress-audit-20260912/` |
| 合成 TS 对照 | 目标 `2,4,7,11,16,21,25,9` 秒；产品硬解/软解错误均 0，关闭选项对照 192；最大落点偏差 21.33 ms | 同上；本轮 TS 报告 |
| 最新源码回归 | 2026-10-18，`d7522cb` 上的工作区快照；Debug/Release 均 0 警告/错误；默认各 298+9 跳过，显式各 307 通过、0 失败/跳过 | [P1-05 记录](evidence/P1-05-01-release-and-phase-1-acceptance.md)；`artifacts/p1-05/` |
| 性能结论 | GTX 1060 HDR→SDR 窗口/全屏由用户此前人工复测确认无问题，PERF-01 已关闭；HEVC/AV1 功能报告中的 105/110 次采样呈现掉帧仍归 PERF-02 跟进 | [P1-01 / PERF-01 用户复测收口](evidence/P1-01-05-gtx1060-user-acceptance.md) |
| 当前发布包 | 2026-10-18 Phase 1 Release 自包含包；基线 `d7522cb` + 未提交工作区的完整源码快照，两次最终目录运行退出码均 0；旧外机包保留历史身份 | [唯一发布入口](release-status.md)、[发布证据](evidence/P1-05-01-release-and-phase-1-acceptance.md) |

阶段起始原生硬件报告在约 6 秒内记录 61/62 帧，用于解码路径与非黑像素验证，不包含性能断言。其呈现掉帧需要按测试运行节奏和真实应用场景分别复核，不能从“188 通过”得出播放流畅结论。P1-04 的功能测试同样不包含吞吐/掉帧通过条件，不关闭 PERF-02。

源码回归、既有包身份和外机复测属于不同证据。包的精确目录、构建时间、提交与哈希以 `release-status.md` 为准，不在各阶段进度表重复维护。

## 3. 工作包状态

| 工作包 | 状态 | 开始 | 完成 | 产物 / 当前结论 |
|---|---|---|---|---|
| P1-00 基线、计划与发布入口 | 通过 | 2026-09-12 | 2026-09-12 | 计划/进度、基线证据与发布入口均已核验；本次只修改文档及解决方案文档索引 |
| P1-01 固定媒体回归与性能场景复核 | 通过 | 2026-09-21 | 2026-10-04 | [P1-01 记录](evidence/P1-01-01-fixed-media-and-performance-scenarios.md)：用户确认 GTX 1060 HDR→SDR 窗口/全屏此前复测正常，PERF-01 关闭；PERF-02 采样对照作为独立跟进项保留 |
| P1-02 状态、错误与命令契约 | 通过 | 2026-10-13 | 2026-10-16 | 已修复加载取消边界、后端积压丢失关键状态及播放列表 redirect 提前失败；[加载取消记录](evidence/P1-02-01-load-cancellation-boundary.md)、[事件与 redirect 记录](evidence/P1-02-02-event-overflow-and-redirect.md)。已补 [错误分类、非渲染终态及 UI 派发合并](evidence/P1-02-03-error-contract-and-ui-dispatch.md)、[seek 竞争与请求等待边界](evidence/P1-02-04-seek-and-request-contract.md)与[跨加载归属审计](evidence/P1-02-05-cross-load-attribution.md)；[验收汇总](evidence/P1-02-06-p1-02-acceptance.md)确认各验收条目满足 |
| P1-03 生命周期、页面重建与退出 | 通过 | 2026-10-16 | 2026-10-16 | [所有权与释放顺序](evidence/P1-03-01-lifecycle-ownership-and-release-order.md)、[应用内场景](evidence/P1-03-02-app-lifecycle-scenarios.md)、[验收汇总](evidence/P1-03-03-p1-03-acceptance.md)：页面卸载/重挂载、恢复中关闭、加载中关闭、恢复中换媒体均通过 |
| P1-04 视频 HLS、字幕与媒体信息验收 | 通过 | 2026-10-18 | 2026-10-18 | [验收记录](evidence/P1-04-01-video-hls-subtitles-and-media-information.md)：固定 VOD、SRT/ASS 像素、多轨、受控缓冲、错误恢复与换媒体信息通过；修复关闭音轨的旧编码残留；保留类型/硬件/性能限制 |
| P1-05 发布可复现与 Phase 1 验收 | 通过 | 2026-10-18 | 2026-10-18 | [验收记录](evidence/P1-05-01-release-and-phase-1-acceptance.md)：两配置显式各 307 通过、原生/许可/源码快照、最终目录播放退出、日志及 691 项 ZIP 校验完成 |

依赖、工作边界、可判定验收和必要验证见 [实施计划](phase-1-plan.md#3-工作包与顺序)。P1-00 通过只表示基线与入口完成，不表示 Phase 1 的代码和产品目标完成。

## 4. 已有能力与下一步缺口

| 领域 | 可承接的现有证据 | 后续边界 |
|---|---|---|
| 架构与原生依赖 | 四生产/四测试项目；锁定来源、补丁、哈希与许可证；所有权及发布对应关系已记录 | 更新依赖时重新核验来源和发布闭包 |
| 播放控制 | P1-02 已通过；命令关联、EOF、错误恢复、缓冲和终态隔离已核验 | P1-05 已按最终源码与发布目录收口 |
| 渲染与恢复 | P1-03 已通过；真实页面重建、暂停帧和资源释放均有证据 | 保留跨屏、实际 TDR、长期资源趋势的未验证范围 |
| 媒体与轨道 | P1-04 已通过；视频 HLS、内嵌 SRT/ASS 像素、多轨与换媒体信息均有证据 | 不扩大到未测 HLS/字幕类型；P1-05 已核对最终产物 |
| 性能 | RTX 3070 限定场景 4K60；GTX 1060 用户复测已关闭 PERF-01 | PERF-02 有采样/无采样对照仍需独立复核 |
| 发布 | P1-05 自包含包、源码快照、许可、闭包及最终目录运行通过 | 新包继续按同样步骤独立验收；本轮未重新执行外机矩阵 |

## 5. 待复核项与延后范围

### 本阶段待复核

- **PERF-01：历史应用内慢帧。已关闭（2026-10-04）。** [2026-09-11 恢复记录](evidence/buffering-and-app-recovery-2026-09-11.md#整段播放结果与未解决的性能问题)曾记录 GTX 1060、4K60 SDR tone-map 约 14 fps；用户确认同一类 HDR→SDR 窗口和全屏场景此前复测正常。关闭依据和日志限制见 [P1-01 / PERF-01 用户复测收口](evidence/P1-01-05-gtx1060-user-acceptance.md)。
- **PERF-02：原生功能报告中的掉帧。** 本轮 HEVC/AV1 的 105/110 次呈现掉帧来源于功能测试报告（前 60 帧后在渲染线程反复 Render + 读回）。P1-01 已定义场景 S4/S5 做"有采样 vs 无采样"对照，待执行。未完成复核前不宣称性能通过。
- **MEDIA-01：媒体覆盖。已关闭（2026-10-18）。** P1-04 补齐本机视频 VOD、SRT/ASS 实际像素、多音轨及换媒体信息；通过范围与未覆盖格式见 [验收记录](evidence/P1-04-01-video-hls-subtitles-and-media-information.md)。
- **LIFE-01：页面生命周期。已关闭（2026-10-16）。** 真实 Frame 导航卸载/重新挂载、恢复中关闭、加载中关闭与重复故障均通过；见 [P1-03 验收汇总](evidence/P1-03-03-p1-03-acceptance.md)。

### 沿用原决定延后

跨屏/双显示器、长期稳定性、真实触屏、100%/125%/200% DPI、Intel/AMD 等额外硬件矩阵以及 HDR 仪器测量继续保留为未验证。它们不成为本阶段新增阻塞，也不由自动化结果推断通过。Windows/驱动支持声明必须与实际验证范围一致。

最近打开持久化、倍速交互、时间轴预览、完整字幕面板和 OSD/触屏视觉交互留待 Phase 2。播放列表、自适应码率策略等若拟加入，先明确产品范围，不能仅因出现在 Phase 0 后续输入中便当作本阶段已承诺功能。

## 6. 验证与变更记录

| 日期 | 工作包 | 事项 | 实际结果 | 证据 / 限制 |
|---|---|---|---|---|
| 2026-09-12 | P1-00 输入 | `f793d0b` 本机 Debug/Release 回归与显式素材审计 | Debug 188 无跳过；Release 184+4 跳过；构建均 0 警告/错误 | [基线证据](evidence/P1-00-01-baseline-and-release-entry.md)；性能未验收 |
| 2026-09-12 | P1-00 | 建立工作包、当前状态与验收边界 | 通过；包内哈希与当前原生锁一致，文本/链接及解决方案解析检查通过 | [基线证据](evidence/P1-00-01-baseline-and-release-entry.md)、[发布状态](release-status.md)；未新增 GUI 验收或重新打包 |
| 2026-09-21 | P1-01 | 建立素材/场景清单、判定规则；新增渲染统计、D3D11 调试层开关、离屏与应用内无采样测量、4K60 合成样片脚本 | 代码与文档已进入工作区；本机（无 .NET/GPU 的沙箱）未构建、未运行测试；目标机构建、回归与全部性能场景待执行 | [P1-01 记录](evidence/P1-01-01-fixed-media-and-performance-scenarios.md)；性能仍未验收 |
| 2026-09-22 | P1-01 | 在 SDR 开发机验证 `ed1295c` 的构建、统计与测量链路 | x64 Debug/Release 构建均 0 警告/错误；各 14 项统计测试 + 1 项自检通过，无失败/跳过；实际渲染设备 GTX 1060，非 WARP | [本次自检记录](evidence/P1-01-01-fixed-media-and-performance-scenarios.md#8-构建与测量链路自检2026-09-22)；未运行全量回归、4K 性能场景或 HDR 显示输出验收 |
| 2026-09-22 | P1-01 | 按用户要求发布 RTX 3070 测试包，附五份素材及测试清单 | Release 渲染/媒体回归 68 通过、无失败/跳过；发布依赖与会话烟雾通过；最终目录 HEVC 播放到 EOF，用户确认画面正常，退出码 0，日志收集与 ZIP 内容哈希通过 | [发布证据](evidence/P1-01-02-rtx3070-test-package.md)；本轮 RTX 3070/HDR 显示输出/性能仍待验证 |
| 2026-10-02 | P1-01 | 复核用户提供的 9 月 22 日 RTX 3070 日志 | 4K 全屏 SDR 59.97～60.26 fps、PQ 60.00～60.12 fps；窗口 HDR→SDR 59.98～60.01 fps；正常 EOF/退出，用户目视无问题 | [复核证据](evidence/P1-01-03-rtx3070-log-review.md)；保留开头丢帧和统计边界 |
| 2026-10-02 | P1-01 | 生成 GTX 1060 独立测试包 | Release 构建 0 警告/错误；常规回归 199 通过、0 失败、5 跳过；发布校验、两份样片 EOF/退出及日志收集通过，ZIP 592 文件核验 | [发布证据](evidence/P1-01-04-gtx1060-test-package.md)；本机日志为 HDR 开启窗口检查，未单独证明 HDR→SDR |
| 2026-10-04 | P1-01 | 用户补充确认 GTX 1060 HDR→SDR 场景 | 用户确认默认窗口和全屏此前测试均无问题；P1-01 / PERF-01 关闭，PERF-02 采样对照保留跟进 | [用户复测收口](evidence/P1-01-05-gtx1060-user-acceptance.md)；关闭依据包含用户此前人工复测，不把 HDR 开启日志包单独当作 tone-map 证据 |
| 2026-10-13 | P1-02 | 开始加载取消边界审计，修复未提交加载提前切换地址及屏蔽原媒体完成事件 | 修复前新增场景 1 通过、2 失败；新增 4 项最终均通过。x64 Debug/Release 构建均 0 警告/错误，各 203 通过、0 失败、5 项未配置的媒体/性能测试跳过 | [加载取消边界记录](evidence/P1-02-01-load-cancellation-boundary.md)；P1-02 整体仍在进行中，未重新发布 |
| 2026-10-13 | P1-02 | 补偿有界队列中丢失的最新播放/轨道/信息/错误/EOF，修复播放列表 redirect 提前结束加载 | 修复前定向 8 失败、4 通过；最终新增 10 项通过。x64 Debug/Release 构建均 0 警告/错误，各 213 通过、0 失败、5 项媒体/性能测试跳过 | [事件与 redirect 记录](evidence/P1-02-02-event-overflow-and-redirect.md)；错误分类、非渲染终态、UI 派发合并待继续 |
| 2026-10-13 | P1-02 | 区分错误类别与恢复方式，保护非渲染终态，限制 UI 派发积压，并补故障后的原生暂停 | 新增 32 项回归通过；x64 Debug/Release 构建均 0 警告/错误，各 245 通过、0 失败、5 项媒体/性能测试跳过 | [错误与 UI 派发记录](evidence/P1-02-03-error-contract-and-ui-dispatch.md)；未做 GUI 人工验收或发布；seek 竞争、取消/超时及跨加载归属契约仍待收口 |
| 2026-10-13 | P1-02 | 修复 seek 回复竞争及失败依赖下的预计位置，补请求取消/超时与真实连续加载边界回归 | 新增 36 项回归通过；x64 Debug/Release 构建均 0 警告/错误，各 281 通过、0 失败、5 项媒体/性能测试跳过 | [seek 与请求契约记录](evidence/P1-02-04-seek-and-request-contract.md)；未做 GUI 人工验收或发布；保留原生内部异步重排的跨加载归属审计，P1-02 整体仍在进行中 |
| 2026-10-16 | P1-02 复验 | 拉取 `e6d33ce` 后本机执行 x64 Debug/Release 构建与全量测试，复核 P1-02 记录的回归基线 | x64 Debug/Release 构建均 0 警告/错误；测试各 281 通过、0 失败、5 项媒体/性能测试跳过，与 2026-10-13 记录一致。首次运行因本机 Codex 沙箱遗留的 Low 完整性标签导致全部 testhost 中止（环境问题，非项目缺陷）；修复标签后重跑，全部通过 | `artifacts/verify-20261016/rerun2/`（run.log、构建/测试日志与 TRX）；5 项跳过为未配置素材或性能输入的测试（HEVC/AV1/PQ/TS seek/自然播放）；未做 GUI 人工验收或发布 |
| 2026-10-16 | P1-02 | 跨加载归属审计：核对 v0.41.0 事件次序与属性合并，修复等待期旧 StartFile 清除新加载等待标记的守卫缺口（原生 playlist_entry_id 归属） | 修复前定向 4 项 1 失败、3 通过（注入的旧 Start/End 对把在载媒体判为中止）；修复后同测试程序集 119 通过、0 失败。全量 x64 Debug/Release 构建均 0 警告/错误，各 285 通过、0 失败、5 项媒体/性能测试跳过 | `artifacts/p1-02-attribution/`（源码核对、misattribution 前后 TRX、debug-final/release-final 日志与 TRX）；[跨加载归属记录](evidence/P1-02-05-cross-load-attribution.md)；未做 GUI 人工验收或发布 |
| 2026-10-16 | P1-02 验收 | 按计划 P1-02 验收条目汇总 P1-02-01～05 证据 | 五条验收条目均满足并在汇总中映射到可复核证据；P1-02 标记通过 | [P1-02 验收汇总](evidence/P1-02-06-p1-02-acceptance.md)；关闭不改变各记录限制（未 GUI 人工验收、未发布） |
| 2026-10-16 | P1-03 输入 | 开始生命周期工作包：审计 UI、命令/事件、渲染三执行域与共享 core 的所有权及释放顺序，核对既有恢复证据 | 审计完成并确定缺口：真实页面卸载/重新挂载无承接路径；释放播放中的 render context 前缺少视频挂起步骤；恢复中关闭/换媒体、加载中关闭缺少场景证据；存在跨域锁顺序隐患待消除 | 实现与场景执行待办，产物记录于 `artifacts/p1-03/`；沿用 P1-01 固定素材与恢复脚本 |
| 2026-10-16 | P1-03 | 固化所有权与释放顺序：新增卸载挂起/重挂载恢复租约、渲染器释放前挂起、Frame 承载页面与应用级关闭收口，消除挂起租约与自动重建的锁顺序隐患 | x64 Debug/Release 构建均 0 警告/错误；全量测试各 293 通过、0 失败、5 项素材测试跳过（LibMpv 123、App 100、Rendering 68/73、Abstractions 2） | `artifacts/p1-03/`（构建与测试日志、TRX）；[所有权记录](evidence/P1-03-01-lifecycle-ownership-and-release-order.md)；保留渲染集成首跑 3 项断言失败与修正历史 |
| 2026-10-16 | P1-03 | 真实页面生命周期场景：Frame 导航卸载/重挂载（播放/暂停）、恢复中关闭、加载中关闭、恢复中换媒体；合成、4K60 HEVC 硬解与含音轨 TS 素材 | 应用内默认 10 场景与 4K/TS 补充 4 场景全部通过；关闭后会话与渲染器均已释放、重复关闭幂等、会话日志释放标记核对通过 | `artifacts/p1-03/app-full-final/`、`app-4k-final/`、`app-ts-final/`；[场景记录](evidence/P1-03-02-app-lifecycle-scenarios.md)；故障为注入，非真实驱动重置 |
| 2026-10-16 | P1-03 复验 | 冻结工作区（含探针 `TracksBefore` 字段）重新构建并全量复跑；处理最终树首跑出现的测试服务器收尾竞态 | 构建均 0 警告/错误；Release 首跑直接通过；Debug 首跑 1 项 `BackendSeekConcurrencyTests` 收尾竞态失败（`HttpMediaServer` 停听与在途 Accept 竞争抛 `ObjectDisposedException`，与本包产品改动无关），补收尾 catch 后两配置复跑均 293 通过、0 失败、5 项素材测试跳过 | `artifacts/p1-03/debug-build-final.log`、`release-build-final.log`、`debug-tests-final3/`、`release-tests-final3/`（失败首跑保留 `debug-tests-final2/`）；[所有权记录](evidence/P1-03-01-lifecycle-ownership-and-release-order.md) §4 |

2026-10-18 / P1-04：从 `d7522cb` 开始补齐固定媒体、真实像素和信息一致性测试；先复现再修复主动关闭音轨的编码残留。最终 x64 Debug/Release 均 0 警告/错误，全量回归各 302 通过、0 失败、5 项既有素材/性能测试跳过，四项新增媒体场景均实际执行。记录与失败历史见 [P1-04 验收](evidence/P1-04-01-video-hls-subtitles-and-media-information.md)，原始产物位于 `artifacts/p1-04/`；未执行 GUI 人工验收，未重新发布。

2026-10-18 / P1-05：最终构建、默认/显式素材回归、发布与源码快照/许可/ZIP 校验完成；两次最终目录运行均 EOF 后退出 0。首次桌面访问失败与 RDP 最小化历史、第二次由用户正常关闭均如实记录；GUI 画面截图与原始日志已保存。工作包全部通过，Phase 1 在声明范围内完成，详见 [发布验收](evidence/P1-05-01-release-and-phase-1-acceptance.md)。

每包结束时追加实际命令、测试配置、素材、提交和证据；保留失败历史。声明范围外的延后项不能因阶段完成而改记为通过。
