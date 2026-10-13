# P1-02：错误契约、终态隔离与 UI 有界派发

- 日期：2026-10-13。
- 输入：`4fcaef4` 加上前两轮尚未提交的修复，见 [加载取消边界](P1-02-01-load-cancellation-boundary.md)及[事件积压与播放列表跳转](P1-02-02-event-overflow-and-redirect.md)。
- 范围：后端错误分类、可恢复错误与不可恢复终态、迟到结果隔离、UI 事件合并及取消清理。
- 原始记录：`artifacts/p1-02-terminal/`；最终 Debug/Release 构建及完整回归结果见第 6 节。
- 本轮未开展 GUI 人工验收、未发布新包；P1-02 整体仍在进行中。

## 1. 修复前与首次修复后的证据

新增场景先在原实现运行，保留失败记录；没有把首次修复后的结果直接写成完整验收通过。

| 阶段 | 范围 | 结果 | 记录 |
|---|---|---|---|
| 修复前 | App 终态与派发定向场景 | 13 项：10 失败、3 通过 | `app-before.trx`、`app-before.log` |
| 修复前 | Backend 错误分类与终态定向场景 | 8 项：7 失败、1 通过 | `backend-before.trx`、`backend-before.log` |
| 首次修复后 | App 定向回归 | 45 项通过、0 失败 | `app-after.trx`、`app-after.log` |
| 首次修复后 | Backend 错误及事件队列回归 | 35 项：34 通过、1 失败 | `backend-after.trx`、`backend-after.log` |
| 独立复核后新增、修复前 | 原生队列溢出时实际暂停 | 1 项失败 | `queue-pause-before.trx`、`queue-pause-before.log` |

App 的缺口包括：会话故障后，已发出的播放、暂停、seek、加载、轨道、音量或静音命令的迟到成功结果仍可改变界面；事件流中断没有建立不可恢复终态；就绪状态突发仍逐条派发。Backend 的缺口包括：错误只有消息文本、会话关闭后仍接受迟到媒体事件，以及关闭后的请求拒绝和加载等待退出缺少一致契约。

首次 Backend 回归中的唯一失败来自既有 `Overflow_repair_should_use_the_surviving_event_snapshot_instead_of_a_later_error` 顺序断言。新契约在队列错误后额外发布停止状态，状态保留相同进度值；旧断言以该进度值的最后一次出现定位错误之前的事件，因此选中了错误之后新增的状态。已修正断言并纳入最终回归；原始 35 项结果仍保留为 34 通过、1 失败。

后续只读复核发现：仅令后端报告停止，不能证明原生 core 已停止播放。新增场景在真实会话中检查 `pause`，修复前实际仍为 `false`，因此独立记录了上述 1 项失败。

## 2. 错误分类与恢复契约

`BackendFaulted` 增加 `PlayerErrorKind Kind` 与派生的 `IsTerminal`。默认类别为 `Playback`，保留已有只传消息调用的可恢复语义；UI 不解析原生错误码或消息文本来决定恢复性。

| 类别 | 典型来源 | `IsTerminal` | 本轮契约 |
|---|---|---|---|
| `Loading` | `FileLoaded` 之前发生媒体读取错误 | `false` | 结束对应加载等待并显示错误；允许重新加载媒体 |
| `Playback` | 已加载媒体的播放错误 | `false` | 显示本次媒体错误；允许重新加载媒体 |
| `EventQueue` | 原生事件队列溢出，状态可能缺失 | `false` | 当前加载等待及时失败，清除播放/缓冲状态；迟到 `FileLoaded` 不能单独恢复，须先看到新 `StartFile`；允许重新加载 |
| `Initialization` | 原生初始化失败，或 ViewModel 初始化失败 | `true` | 保留原始故障，拒绝继续初始化与播放操作；需要重新启动播放器 |
| `Session` | 原生会话关闭或运行循环失败 | `true` | 保留停止状态，拒绝后续播放修改；需要重新启动播放器 |
| `EventStream` | 原生事件解析、UI 观察或应用失败 | `true` | 停止 UI 状态推进并保留原始故障；需要重新启动播放器 |
| `Rendering` | 不可恢复渲染错误的分类值 | `true` | 分类与既有渲染终态语义一致；现有渲染器仍通过专用故障通知路径处理 |

后端终态在状态锁内建立：完成仍在等待的加载，清除播放与缓冲状态，发布最终状态和分类故障，之后忽略迟到的媒体、属性及 EOF 事件。播放修改入口检查终态；关闭释放及已有信息快照读取仍有独立用途，不依赖重新启动事件处理。

可恢复错误不建立永久锁存。原生事件溢出可能意味着 `FileLoaded` 或 `EndFile` 已经丢失，不能让加载调用继续等到超时，也不能把旧 `FileLoaded` 当成已经恢复。重新加载进入新的媒体事件周期后才恢复正常状态推进。

ViewModel 锁存不可恢复故障，清除缓冲、显示控件并保持停止状态。播放、seek、加载、轨道及会话故障下的音量/静音操作，在请求前与异步成功返回后均检查终态；迟到结果不能覆盖状态、轨道、最近打开记录或原始错误。页面在终态通知时停用播放相关控件，并在初始化及视频附着后的关键边界检查终态。既有 `ReportRenderingFailure` 路径保留专用提示，ViewModel 逻辑层仍允许音量/静音调用，真实页面则禁用全部播放控件；不把该声音操作例外扩展到已停止的原生会话，也不宣称所有 Rendering 路径均由 ViewModel 完全拒绝命令。

`MpvPlayerSession` 收到原生 Shutdown 后标记会话失效并退出事件循环。解析异常按 `EventStream` 报告；运行循环外层按初始化是否完成区分 `Initialization` 与 `Session`。退出清理先完成 pending 请求，再等待渲染上下文归还，继续遵守 context 先于 core 的释放顺序。

## 3. UI 有界派发与取消清理

`UiPlayerEventPump` 每批最多消费 **64 个源事件**。一个连续状态段内，播放状态、轨道、媒体信息、缓冲各保留最新值，按它们最后出现的顺序应用；错误、EOF 以及其他离散事件构成分段边界，不跨边界合并。

事件泵至多保留 **1 次挂起读取、1 个等待完成的 UI 回调**。当前 UI 批次未完成时不继续无限排空后端，剩余事件留在后端容量 256 的队列中，继续使用上一轮的发布时快照补偿。批量限制按源事件计数，而不是只按合并后条目数计数，因此持续到来的同类属性也不能无限延长一批。

没有下一项同步就绪事件时立即派发已有批次，不等下一次属性变化才刷新；下一次读取已经失败时，也先应用此前已读取的事件，再传播事件流故障。取消后，排队 UI 回调检查 token 并忽略迟到更新。

清理使用关联的读取 token：先请求取消挂起读取，等待该次 `MoveNextAsync` 结束，再调用枚举器 `DisposeAsync`，避免同时读取与释放 async iterator。真实后端通道支持该取消；这不是对任意忽略取消的第三方枚举器作无限等待保证。

## 4. 独立复核发现与追加实现

以下两项是在首次定向回归后发现并追加处理的实现；相关回归已纳入第 6 节两个配置的完整验证，首次定向通过不代表当时已经覆盖这些缺口。

1. **队列错误必须影响实际播放。** 后端把 `IsPlaying` 变为 false 后，若原生 core 继续发声，界面的播放按钮又因媒体不处于 loaded 状态而无法正常暂停。原生 QueueOverflow 派发路径追加尽力提交 `pause=true`；真实 core 回归确认实际暂停，重新加载后可恢复播放。运行循环和解析异常的退出路径同样追加尽力暂停，避免等待渲染资源归还时继续播放音频；该异常退出路径没有通过自然原生故障作 GUI 验收。
2. **UI 观察失败不能只锁住界面。** UI 应用失败时，后端 core 可能仍正常播放；已追加使用事件泵取消 token 的尽力 `PauseAsync`，暂停失败只记录日志，不覆盖原始终态原因。dispatcher 同步拒绝故障通知时，异常被捕获并记录，事件泵正常结束，不跨线程修改 UI，也不让通知异常跳过后端释放。暂停成功/失败两种原始错误保护场景，以及拒绝派发后 backend 仍可释放的场景均在最终回归通过，无需额外改动 Dispose 的 finally 结构。

## 5. 测试层级及证据限制

| 层级 | 做法 | 能支持的结论 | 不能据此宣称的结论 |
|---|---|---|---|
| 后端托管事件注入 | 对 `OnSessionEvent` 注入 Shutdown、队列错误、媒体错误及迟到属性 | 指定输入序列下的分类、终态隔离、状态恢复行为 | 原生一定自然产生该顺序，或已复现所有跨加载竞态 |
| App 与事件泵受控测试 | 假后端、受控命令完成、受控 UI 回调、失败读取 | 迟到成功隔离、合并顺序、批量上限、取消及异步枚举清理 | WinUI 实际渲染、界面人工体验或系统线程调度性能 |
| 真实原生会话 | 非法初始化选项、真实 `quit`、本机 HTTP WAV 加载 | 真实初始化/关闭行为，以及同一 core 的加载和播放恢复 | 视频 HLS、硬件解码或显卡表现 |
| 真实 core 加人工溢出触发 | 在真实事件线程的提交回调中调用既有 Dispatch 处理 QueueOverflow，再读取原生 `pause` 并重新加载 | 原生溢出处理分支确实向实际 core 提交暂停，并能在重新加载后恢复 | 通过真实负载自然触发了 mpv 原生事件队列溢出 |

本轮没有以真实负载自然制造 mpv 原生队列溢出；也没有把真实 core 加人工触发的测试写成自然溢出验收。测试使用本机生成素材及回环 HTTP 服务，没有新增外部媒体依赖。

## 6. 最终验证

在 Windows x64 本机执行 Debug/Release 构建及完整测试。本轮新增 **32 项**全部运行通过：LibMpv 新增 11 项；App 新增 21 项，包括 ViewModel 终态 14 项、批派发 1 项、事件泵 6 项。追加的实际暂停、暂停失败保留原错误、dispatcher 拒绝后释放，以及修订后的旧顺序断言均包含在最终结果中。

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p1-02-terminal/debug
dotnet build mpv-winui.slnx -p:Platform=x64 -c Release --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 -c Release --no-build --no-restore --logger trx --results-directory artifacts/p1-02-terminal/release
```

| 验证 | Debug | Release |
|---|---|---|
| 解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| Abstractions | 2 通过 | 2 通过 |
| LibMpv | 104 通过 | 104 通过 |
| App | 75 通过 | 75 通过 |
| Rendering.WinUI | 64 通过、5 跳过 | 64 通过、5 跳过 |
| 总计 | **245 通过、0 失败、5 跳过** | **245 通过、0 失败、5 跳过** |

最终日志为 `build-debug.log`、`build-release.log`、`test-debug.log`、`test-release.log`，TRX 位于 `debug/`、`release/`。五项跳过仍为 HEVC、AV1、PQ 渐变、TS seek 对照及自然播放性能场景；未配置对应外部媒体输入，不把这些跳过解释为新验收通过。

## 7. 未收口范围

- P1-02 的 seek 竞争、命令提交前后取消/超时的完整组合，以及跨加载媒体事件归属仍未收口；已有针对性测试不能代替整个工作包验收。
- 本轮没有开展真实 GUI 人工验收，没有重新发布，也没有新增 HEVC、AV1、HDR、TS seek 或自然播放性能结论。
- 页面重建与退出的完整生命周期验收、视频 HLS/字幕、PERF-02 及发布工作继续按各自工作包推进。
