# P1-02：跨加载归属审计与原生条目 ID 修复

- 日期：2026-10-16。
- 输入：`e6d33ce` 加上此前尚未提交的 P1-02 修复；承接[加载取消边界](P1-02-01-load-cancellation-boundary.md)、[事件积压与播放列表跳转](P1-02-02-event-overflow-and-redirect.md)、[错误契约与 UI 派发](P1-02-03-error-contract-and-ui-dispatch.md)、[seek 与请求契约](P1-02-04-seek-and-request-contract.md)。
- 范围：原生 StartFile / EndFile 在连续加载与取消等待下的归属结论；等待期旧 StartFile 清除新加载等待标记的守卫缺口；以原生 `playlist_entry_id` 建立归属的修复；修复前后定向证据与最终全量回归。
- 原始记录：`artifacts/p1-02-attribution/`（v0.41.0 源码核对在 `mpv-src/`、`mpv-full/`；修复前失败与修复后通过 TRX 在 `misattribution/`；最终构建与测试日志、TRX 在 `debug-final/`、`release-final/`）。
- 本轮未开展 GUI 人工验收或重新发布；P1-02 的验收映射见 [P1-02-06](P1-02-06-p1-02-acceptance.md)。

## 1. 方法与来源

审计对象为锁定的 mpv v0.41.0（提交 `41f6a645068483470267271e1d09966ca3b9f413`，见 `build/native/source-lock.json`）；下文行号均为该提交的 `player/`、`include/mpv/client.h`。三类手段互补使用：

| 手段 | 支持的结论 | 不能支持的结论 |
|---|---|---|
| 源码核对 | 事件产生点、次序、客户端队列与属性合并语义 | 运行时实际送达次序（受调度影响） |
| 真实会话探针 | 指定屏障与命令压力下真实事件序列、加载存活 | 证明原生不存在其他内部重排 |
| 托管事件注入 | 指定输入序列下的守卫语义与终态行为 | 宣称原生一定自然产生该顺序 |

## 2. 源码结论：严格交替与归属材料

- `START_FILE` 只在 `play_current_file` 头部发出（loadfile.c:1648），`FILE_LOADED` 只在播放启动成功路径发出（loadfile.c:1869），`END_FILE` 只在退出该条目时发出（loadfile.c:1995-2000）。三者由核心线程在 `mp_play_files` 的逐条目串行循环中同步产生，因此真实序列严格为 Start → (FileLoaded) → End 交替，旧媒体 End 必然先于新媒体 Start。
- 客户端事件按产生顺序入队并依序送达（client.c:767 广播；`mpv_wait_event` 依序弹出）。属性变更只在队列为空时合并生成、取生成时的新值（client.h 文档注释；client.c:892、1619、1669；playloop.c:55）。因此旧媒体的迟到属性值不会排到已入队的 StartFile 之后重放。
- `loadfile` 的命令回复携带新条目的原生 `playlist_entry_id`（command.c:6116 结果节点；client.c:1184 `async_cmd_complete` 复制进 `mpv_event_command`，client.h:1571-1579）。`StartFile`、`EndFile` 事件也携带该字段（client.h:1497-1524，自 API 1.108）。这是上游为跨加载归属提供的稳定材料。

## 3. 守卫缺口与可达窗口

`LibMpvBackend` 提交 `loadfile` 后进入 `_awaitingStart`，并丢弃排队中的旧 `EndFile` / `FileLoaded`。该守卫隐含假设“等待期送达的 StartFile 必属新媒体”，而这一假设并不由源码保证：当核心线程在处理完下一次加载的 `pause` 回复、尚未执行到 `play_current_file` 的间隙被调度抢占时，旧条目的 `StartFile` 会排在新加载受理之后入队。旧 StartFile 在等待期送达即清除等待标记，紧随其后的旧 `EndFile` 不再被丢弃，把仍在加载的新媒体判为中止（`媒体加载已中止。`），旧媒体事件由此覆盖新媒体状态。

上一轮只验证了反向次序下的消费屏障（[P1-02-04](P1-02-04-seek-and-request-contract.md) 第 5 节第 2 项），未覆盖此窗口；本轮压力探针（40 次快速取消替换、60×2 次填充替换）在修复前也未自然命中它——该窗口依赖操作系统抢占时机。修复因此不试图“命中”该时序，而是用原生条目 ID 使归属不再依赖送达时机。

## 4. 修复：原生条目 ID 归属

| 文件 | 变更 |
|---|---|
| `Native/MpvNative.cs` | 新增 `MpvEventStartFile`（client.h:1497）与 `MpvEventCommand`（client.h:1571）结构，均为 internal |
| `MpvPlayerSession.cs` | loadfile 回复解析结果节点并发布 `CommandReply` 事件（含 `PlaylistEntryId`，先于请求等待完成）；`StartFile` / `EndFile` 事件携带原生条目 ID |
| `LibMpvBackend.cs` | 新增 `_pendingEntryId`：提交新加载或事件队列溢出时清零，收到 loadfile 回复时登记；等待期只接受与期望一致 ID 的 StartFile（`if (_awaitingStart && playerEvent.PlaylistEntryId != _pendingEntryId) return;`） |

归属规则的效果：等待期送达的任何无法与期望 ID 对应的 StartFile（旧条目 ID 不匹配，或无 ID 的注入事件）都不再清除等待标记，旧 EndFile 继续被丢弃；新媒体自身的 StartFile 因命令回复先于事件送达（同一队列、同一线程）而必然匹配。修复前 `SessionEvent` 没有归属字段，测试无法表达带 ID 的注入；修复后同一测试以无 ID 的旧 Start/End 对表达“无法归属到当前加载的 Start”，原生旧 Start 携带的不匹配真实 ID 由同一比较丢弃。

## 5. 修复前后定向证据

`BackendCrossLoadAttributionTests` 新增 4 项（2 项探针理论 + 1 项托管注入 + 1 项压力）：

| 场景 | 修复前 | 修复后 |
|---|---|---|
| 连续替换探针（60 次 × 无/48 条填充命令）：加载受控媒体后取消等待、立刻加载本机媒体；断言新加载不失败、原生路径正确、StartFile/EndFile 严格交替 | 通过 | 通过 |
| 托管注入：新加载受理、其 StartFile 被屏障拦住时，注入旧媒体的 Start/End 对 | 失败：`InvalidOperationException : 媒体加载已中止。`（旧 Start 清除了等待标记） | 通过 |
| 快速取消替换压力（40 次：填充 192 条命令后立刻取消并替换） | 通过 | 通过 |

修复前定向结果：4 项中 1 失败、3 通过（`misattribution/attribution-before.trx`、`attribution-before.log`）；修复后同一测试程序集 119 通过、0 失败（`misattribution/attribution-after.trx`、`attribution-after.log`）。探针与压力两项在修复前后均通过，作为回归保留，不作为该缺口的复现证据。

## 6. 最终验证

最终 x64 Debug/Release 构建与完整测试已完成；日志与 TRX 保留在 `artifacts/p1-02-attribution/debug-final/`、`release-final/`。本轮新增 4 项；既有跨加载、取消、redirect、请求契约与缓冲回归以及 P1-01 固定素材原生集成在两种配置下同时执行，覆盖真实事件次序变更要求的集成复核。

| 验证 | Debug | Release |
|---|---|---|
| 解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| Abstractions | 2 通过 | 2 通过 |
| LibMpv | 119 通过 | 119 通过 |
| App | 100 通过 | 100 通过 |
| Rendering.WinUI | 64 通过、5 跳过 | 64 通过、5 跳过 |
| 总计 | 285 通过、0 失败、5 跳过 | 285 通过、0 失败、5 跳过 |

执行命令：

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p1-02-attribution/debug-final
dotnet build mpv-winui.slnx -p:Platform=x64 -c Release --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 -c Release --no-build --no-restore --logger trx --results-directory artifacts/p1-02-attribution/release-final
```

5 项跳过均因未配置 HEVC、AV1、PQ 渐变、TS seek 对照及自然播放性能测试的外部素材或输入（与 [P1-02-04](P1-02-04-seek-and-request-contract.md) 相同的既有边界），不据此新增硬件或性能结论。

## 7. 未收口与限制

- 原生内部重排窗口本身未在探针中自然复现（依赖调度抢占）；本轮消除的是对送达时序的依赖，而不是已观测到该时序。
- 托管注入只验证指定输入序列下的守卫语义，不宣称原生必然自然产生该顺序。
- 事件队列溢出时归属期望同时清零；溢出后旧 StartFile 与旧 FileLoaded 一样不能单独恢复等待，符合既有“必须重新加载”的姿态。
- 属性合并与缓冲补偿等既有结论不重复证明，沿用前四份记录。
- 未做 GUI 人工验收、未重新发布。
