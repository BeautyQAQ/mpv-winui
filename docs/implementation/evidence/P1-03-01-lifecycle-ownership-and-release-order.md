# P1-03：资源所有权、释放顺序与页面生命周期交接

- 日期：2026-10-16。
- 输入：`9c61935`（P1-02 收口），开始时工作区干净。
- 范围：按 [Phase 1 计划](../phase-1-plan.md#p1-03生命周期页面重建与退出) 固化 UI、命令/事件、渲染三执行域及共享 core 的所有权与启动/关闭/恢复顺序；补齐真实页面卸载/重新挂载的承接路径；修复释放活动 render context 前的视频挂起缺口；消除挂起租约与自动重建之间的跨域锁顺序隐患。
- 原始记录：`artifacts/p1-03/`（构建日志 `debug-build.log`、`release-build.log`；测试日志与 TRX 在 `debug-tests-final/`、`release-tests/`、`debug-rendering-2/`；最终收口复验见 §4）；应用内场景证据见 [P1-03-02](P1-03-02-app-lifecycle-scenarios.md)。

## 1. 三执行域与共享 core 的所有权

| 资源 | 创建者 | 释放者 | 访问约束 |
|---|---|---|---|
| mpv handle / core | 事件/命令线程（`MpvPlayerSession.Run`） | 事件线程 finally；先等待 render context 归还（`_renderReleased`）后才 `mpv_terminate_destroy` | Client API 只在事件/命令线程执行 |
| libmpv render context | 渲染线程（`CreateGraphics`） | 渲染线程（`ReleaseGraphics`）：注销更新回调 → `mpv_render_context_free` → `GC.KeepAlive` 回调委托 | `MpvRenderContext.EnsureThread` 强制创建线程 |
| EGL/ANGLE 上下文与纹理表面 | 渲染线程（`AngleContext`） | 渲染线程；顺序：parking current → render context → EGL → SwapChain → D3D11 | `AngleContext.VerifyThread`；`ResizeBuffers` 前先解除 EGL 对后备缓冲引用 |
| Composition SwapChain | 渲染线程 | 渲染线程；释放前由 UI 线程 `SetSwapChain(0)` 解绑 | `SetSwapChain` 静态入口拒绝非 UI 线程 |
| 面板 SwapChain 绑定 | UI 线程（经 `OnUiAsync` 调度） | UI 线程置空 | `ISwapChainPanelNative.SetSwapChain` 只在 UI 线程 |
| 会话唤醒回调 | `Run` 注册 | `Run` finally 先注销再销毁 handle | 回调只 `Set()` 信号，不调 Client API、异常不越过 ABI |
| 播放会话（core+VM+后端） | DI 单例，进程级 | 应用关闭：`MainWindow.ShutdownPlayerAsync` → `PlayerViewModel.DisposeAsync` → 后端 → 会话 | 页面卸载不销毁会话 |
| 页面资源（渲染器、计时器、事件订阅） | Frame 导航创建（UI 线程） | 页面卸载 `PlayerPage.ReleaseAsync`（幂等） | 卸载先挂起视频再释放 render context |

**启动顺序（首个页面）**：MainWindow 构造时 Frame 导航到 `PlayerPage` → `OnLoaded` → VM 初始化（事件线程创建 core）→ 渲染器初始化（渲染线程创建 D3D11/ANGLE/SwapChain/render context）→ 附加面板（渲染线程建链 → UI 线程 `SetSwapChain` → 开呈现）→ 恢复挂起视频（首次为 no-op）→ `_ready` → 启动媒体参数加载（仅首个页面实例消费）。

**页面卸载顺序（导航离开，新）**：`Unloaded` → `ReleaseAsync`（幂等）：停计时器与输出刷新 → 等初始化任务结束 → 退订 XamlRoot/AppWindow/显示器/VM/渲染器事件 → 渲染器 `DisposeAsync`（持 `_lifecycle`）：**先 `SuspendVideoForDetachAsync`**（暂停+停用视频轨、记录媒体/位置/暂停态，持有播放修改租约）→ UI 线程 `SetSwapChain(0)` → 释放 render context → EGL → SwapChain → D3D11 → 渲染线程退出。会话与 VM 保持存活。

**重新挂载顺序（新）**：新 `PlayerPage` 实例 `OnLoaded` → VM 初始化（已初始化，幂等）→ 渲染器新建并附加 → `ResumeVideoAfterDetachAsync`（校验同一媒体后：恢复视频轨 → 精确 seek 到记录位置 → 恢复暂停状态，归还租约）→ `_ready`。挂起期间的普通播放修改排队等待租约；关闭流程不等待租约归还。

**应用关闭顺序**：`AppWindow.Closing` → `ShutdownPlayerAsync`（幂等）：当前页面 `ReleaseAsync`（若已卸载则跳过）→ VM 销毁 → 后端销毁 → 会话销毁（等 render context 已归还 → 停止事件线程 → 注销唤醒回调 → `mpv_terminate_destroy`）→ `Close()` → 会话日志排空关闭。

**图形故障恢复顺序（P0/P1-02 既有，未变）**：渲染线程故障 → 恢复任务持 `_lifecycle`：停呈现 → 解绑面板 → `SuspendVideoForRecoveryAsync`（暂停+停用+租约）→ 重建图形链 → `ResumeVideoAfterRecoveryAsync` → 恢复呈现；再次失败进入终态（暂停媒体、封锁播放修改）。

## 2. 缺口与修复

| 缺口 | 影响 | 修复 |
|---|---|---|
| 页面卸载即销毁共享会话，没有重新挂载路径 | 无法验证真实页面生命周期；重建页面只能重开进程 | 页面释放与场景销毁分离：`ReleaseAsync` 只释放页面资源；应用关闭统一走 `MainWindow.ShutdownPlayerAsync`；`MainWindow` 改用 Frame 导航承载页面 |
| 释放活动 render context 没有视频挂起步骤 | mpv 将视频轨标记为失败，只有视频的媒体被终止，页面重建无法保留媒体 | `D3D11VideoSurfaceRenderer.DisposeAsync` 在 `_lifecycle` 内先 `SuspendVideoForDetachAsync` 再释放 |
| 锁顺序隐患：页面卸载挂起（先取播放租约）与自动重建（先取 `_lifecycle` 再取租约）可形成环 | 恢复进行中关闭可能互相等待 | 挂起并入渲染器释放路径，统一为 `_lifecycle` → 播放租约的获取顺序 |
| 启动媒体参数在页面重新挂载时重放 | 重建会重新加载启动参数指定的媒体 | 启动参数只由首个页面实例消费（静态标记） |

改动文件：`MpvPlayerSession.PageLifecycle.cs`（新：挂起/恢复租约）、`D3D11VideoSurfaceRenderer.cs`、`MainWindow.xaml(.cs)`、`PlayerPage.xaml.cs`、`BlankPage.xaml(.cs)`（新：中性卸载页面）、`AppRecoveryProbe.cs`、`test-app-recovery.ps1`、`build/testing/README.md`。

## 3. 跨域同步等待审计

- 唯一的同步跨域等待是事件线程退出时等待 `_renderReleased`（`Run` 的 finally，同步 `GetResult`）：不持有任何原生锁，渲染线程从不等待事件线程，等待由渲染资源释放或会话销毁推进，不会成环。
- 其余跨域调用全部异步：UI → 渲染线程经 `RenderWorker.InvokeAsync`；渲染/恢复任务 → UI 线程经 `DispatcherQueue`（`OnUiAsync`）；UI → 播放控制经命令队列与请求等待（超时/取消有界）。
- `SetSwapChain` 的两个调用点（附加、解绑）都经 UI 调度并带线程断言；恢复与输出重配路径同样受 `_lifecycle` 串行化。

## 4. 测试与修复历史

新增测试：`VideoDetachLeaseTests`（3 项：无媒体 no-op 不持租约、重复挂起/恢复幂等、终态不持租约且不重开播放）、`RendererLifecycleIntegrationTests`（4 项：播放中释放挂起与新渲染器恢复、暂停释放位置一致、卸载未恢复即退出可释放、终态释放跳过挂起且幂等）、`BackendLoadCancellationTests` 增加"加载进行中释放后端"1 项。

首跑修复历史（保留）：`RendererLifecycleIntegrationTests` 首跑 3 项失败，原因是断言口径而非实现缺陷——停用后的 `vid` 经节点读取表示为布尔 `false`（而非字符串 `no`）；终态释放活动视频链会终止只有视频的媒体并把 `vid` 复位为 `auto`。修正断言后同程序集 68 通过、5 项素材测试跳过。

最终树收口复验（含未提交的探针 `TracksBefore` 报告字段，重跑后随本包提交）：Release 直接通过（`release-tests-final2/`）；Debug 最终树首跑出现 1 项测试基础设施收尾竞态失败（`debug-tests-final2/`，保留）——`BackendSeekConcurrencyTests` 的 `HttpMediaServer.DisposeAsync` 在 `Stop()` 与在途 `AcceptTcpClientAsync` 竞争时抛出 `ObjectDisposedException`，未被既有 catch 覆盖，与本包产品改动无关；为该接受循环补 `catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)` 后复跑，两配置各 293 通过、0 失败、5 跳过（`debug-tests-final3/`、`release-tests-final3/`；构建日志 `debug-build-final.log`、`release-build-final.log`，均 0 警告、0 错误）。

| 验证 | Debug | Release |
|---|---|---|
| 解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| Abstractions | 2 通过 | 2 通过 |
| LibMpv | 123 通过 | 123 通过 |
| App | 100 通过 | 100 通过 |
| Rendering.WinUI | 68 通过、5 跳过 | 68 通过、5 跳过 |
| 总计 | 293 通过、0 失败、5 跳过 | 293 通过、0 失败、5 跳过 |

5 项跳过为未配置 HEVC/AV1/PQ/TS/性能输入的素材驱动测试（与 P1-02 相同的既有边界）。本机为 GTX 1060 / Windows 11，Rendering 集成测试走真实显卡路径。

## 5. 未验证与限制

- 跨屏、真实 TDR/驱动重置、长期资源趋势未测；本包证据来自注入故障与真实页面导航，不能替代实际驱动重置验证。
- 页面挂起期间播放修改排队等待，直到重新挂载恢复或会话销毁；当前产品只有一个页面，该语义由探针驱动验证，多页面交互留待 Phase 2。
- 未做 GUI 人工目视验收与重新发布。
