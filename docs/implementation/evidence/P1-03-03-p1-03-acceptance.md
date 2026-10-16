# P1-03 验收汇总：生命周期、页面重建与退出

- 日期：2026-10-16。
- 范围：按 [Phase 1 计划](../phase-1-plan.md#4-工作包细则) 中 P1-03 的验收条目汇总本轮可复核结论；不重复执行已验证内容，不改写各记录的限制。
- 依据：[P1-03-01 所有权与释放顺序](P1-03-01-lifecycle-ownership-and-release-order.md)、[P1-03-02 应用内场景](P1-03-02-app-lifecycle-scenarios.md)，以及本次重跑的既有恢复场景与全量回归。

| 计划验收条目 | 结论与依据 |
|---|---|
| `SetSwapChain` 在 UI 线程，EGL/OpenGL 与渲染资源由渲染线程管理；执行域间无同步循环等待 | 满足：`SetSwapChain` 静态入口拒绝非 UI 线程，附加、解绑与输出重配/恢复重建全部经 UI 调度执行；`AngleContext`/`MpvRenderContext` 强制创建线程，`RenderWorkerTests` 验证专用渲染线程与队列顺序。跨域等待审计见 P1-03-01 §3：唯一同步等待是事件线程退出时等待 render context 归还，不成环；页面重建在真实窗口完成旧页解绑与新页绑定，恢复进行中关闭在其重建步骤被显式持有 2 秒期间仍于 2.169 秒内完成关闭。 |
| render context 先于 core 释放，回调注销前所有者仍有效；重复关闭幂等，关闭后排队事件不更新页面 | 满足：会话销毁先等待 render context 归还（`_renderReleased`），唤醒回调注销后才 `mpv_terminate_destroy`；render context 释放顺序为注销更新回调 → `mpv_render_context_free` → `GC.KeepAlive` 回调委托。重复关闭：会话 100 次循环含双重释放（既有回归）、渲染器双重释放（新增集成测试）、全部应用内场景报告 `ShutdownRepeatSafe=true`。关闭后事件：P1-02 的 `PlayerViewModelTerminalTests`/`UiPlayerEventPumpTests` 覆盖取消事件泵与忽略已排队 UI 回调，页面释放时退订全部事件。 |
| 播放和暂停状态下页面重建、渲染重建均保持正确媒体/轨道；暂停场景核对稳定位置和真实像素，不仅检查 Present 计数 | 满足：`page-rebuild-playing`/`page-rebuild-paused` 在合成素材、4K60 HEVC 硬解素材与含音轨 TS 素材上通过——媒体路径与轨道保持（TS 场景报告记录重建前后轨道清单恒等）、卸载窗口受控暂停（位置不前进）、暂停原生位置一致（差 0.001 秒内）、暂停画面像素差为 0（同一帧），播放重建后画面继续变化（最终三种素材测得 0.0033、0.0261、0.0323，均高于 0.001 门槛）且帧持续呈现。渲染重建由既有 `playing`/`paused` 场景在同一进程模型下重跑通过。 |
| 首帧再次故障、重建失败、恢复中换媒体/关闭均不会恢复旧媒体或清除终态错误；退出完成且没有遗留活动会话 | 满足：`immediate-failure`/`rebuild-failure` 重跑到达终态且稳定（终态稳定性的命令与显示变化断言沿用探针既有检查）；`recovery-media-switch` 换入文件成为最终媒体、无错误提示、画面继续变化；`close-during-recovery`/`close-during-load` 正常关闭。应用内全部场景运行（默认 10 项、4K 与 TS 素材补充各 2 项）均确认 `SessionReleased`/`RendererReleased`、退出码 0，且会话日志包含“会话已销毁”与图形资源按顺序释放标记；库层另有“卸载未恢复即退出”“加载中释放后端”2 项测试。 |
| 证据明确区分故障注入和真实驱动重置；跨屏、实际 TDR 与长期资源趋势未测保持未验证 | 满足：P1-03-01 §5 与场景记录均明确注入故障不代表真实 TDR；跨屏、实际驱动重置、长期资源趋势保持未验证，未扩大硬件承诺。 |

**必要验证**：运行了 `VideoDetachLeaseTests`、`RendererLifecycleIntegrationTests`、`BackendLoadCancellationTests` 及全量回归；应用内通过 `test-app-recovery.ps1` 执行全部 10 个默认场景与 4K、TS 素材补充场景，命令与报告见 [P1-03-02](P1-03-02-app-lifecycle-scenarios.md)。代码受影响，最终执行 x64 Debug/Release 构建（均 0 警告、0 错误）与全量测试（各 293 通过、0 失败、5 项素材测试跳过；Debug 最终树首跑 1 项测试服务器收尾竞态失败已修复并复跑通过，见 [P1-03-01 §4](P1-03-01-lifecycle-ownership-and-release-order.md)）。

**结论与限制**：P1-03 在计划声明范围内满足全部验收条目，进度表标记通过。关闭不改变各记录的限制：未做 GUI 人工目视验收、未重新发布；注入故障不等于真实驱动重置；PERF-02、视频 HLS/字幕与最终发布分别由跟进项与 P1-04/P1-05 处理。
