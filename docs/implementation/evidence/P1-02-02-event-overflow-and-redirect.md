# P1-02：事件积压补偿与播放列表跳转

- 日期：2026-10-13。
- 输入：`4fcaef4` 加上一轮尚未提交的加载取消边界修复，见 [上一轮记录](P1-02-01-load-cancellation-boundary.md)。
- 范围：后端有界事件队列的状态丢失、播放列表 redirect 的加载完成语义。
- 原始记录：`artifacts/p1-02-events/`。本轮没有发布新包；P1-02 整体仍在进行中。

## 1. 修复前证据

定向选择新增事件积压场景、原有溢出场景及播放列表场景，**12 项中 8 失败、4 通过**，见 `events-before.trx` 和 `events-before.log`。

- 5 个新增积压场景均失败：轨道/媒体信息/错误丢失；EOF 丢失；只剩信息事件时停止状态丢失；较早错误被丢掉而后来的错误仍存活；换媒体清空轨道/信息的事件丢失。
- 普通、嵌套 M3U 加载及损坏子媒体错误传播共 3 项失败。`EndFile` 的 redirect 被提前转换为“媒体加载已中止”，即使实际子媒体随后成功加载。
- 3 个原有溢出回归和空播放列表边界通过。

测试全部使用本机生成的 WAV/M3U，或向后端注入已复制的托管事件。队列缺陷通过暂停消费后发布超过 256 项事件复现，没有依赖 UI 调度速度，也没有把任意注入时序当成原生媒体归属竞态证据。

## 2. 事件队列契约

`PlayerEventBuffer` 继续使用容量 256 的 `DropOldest` 通道。每次发布附带不可变快照，固定保存播放状态、轨道、媒体信息、错误和 EOF 五类的最新事件及发布序号；每个事件同时保存当时的缓冲状态。

| 条件 | 行为 |
|---|---|
| 没有丢失事件 | 保持原有事件次序及缓冲转换 |
| 消费时发现发布序号缺口 | 从当前存活事件的快照恢复缺口内各类最新事件，按原发布序号排序，再交付存活事件 |
| 错误文本相同但再次发生 | 依靠序号区分，不因文本相同而忽略新的故障；过载仍以该类最新事件为准 |
| 同一次连续观察中再次积压 | 不重发该消费者已经收到的错误或 EOF |
| 新加载成功提交或 `StartFile` | 清空旧媒体补偿快照，旧错误/EOF 不进入新媒体快照 |
| keep-open EOF 后 seek 恢复 | `eof-reached=false` 且媒体仍加载时清除 EOF 补偿，之后再次到尾可发新 EOF |

补偿使用发布时快照，不读取后端当前全局值，因此不会把未来错误或未来媒体信息插到更早的存活事件前。缓冲转换按各个补偿事件的快照还原，错误/EOF 前需要的缓冲结束仍可送达。初次轨道/信息和换媒体清空信息即使被挤掉，也能由后续存活事件恢复。

队列及每份快照的槽位数固定，快照不引用历史快照链。过载时保证可恢复每类最新状态，不承诺保留每条历史进度或所有重复故障；诊断历史仍由日志提供。关闭枚举后重新订阅属于新的观察，可能重新同步快照，本轮未扩展成多消费者或跨订阅去重契约。

## 3. 播放列表 redirect 契约

上游 [v0.41.0 Client API 的 EndFile 定义](https://github.com/mpv-player/mpv/blob/v0.41.0/include/mpv/client.h) 说明：播放列表展开会发送 redirect，实际播放随后继续进入插入条目；插入条目数给出是否存在后续条目的依据。已核对本地缓存 `artifacts/p1-02-audit/client.h` 的对应定义。

沿用已有 `MpvEndFile` ABI，把 `PlaylistInsertCount` 复制到内部 `SessionEvent`。后端仅在 reason 为 redirect 且插入数大于 0 时继续等待实际 `FileLoaded`；空列表及时失败；子媒体损坏时把实际 `MpvException(-17)` 返回给加载调用方。普通及嵌套 M3U 的 `CurrentUrl` 仍保留用户打开的入口路径，原生 `path` 为实际 WAV。

没有新增原生入口或改变结构体布局。这是已有加载接口的正确性修复，不新增播放列表 UI，也不替代视频 HLS 验收。

## 4. 验证结果

首次修复后，全部 `BackendBufferingEventTests` 与 `BackendPlaylistRedirectTests` 共 **28 项通过、0 失败、0 跳过**，见 `events-after.trx`。随后增加“相同文本错误发生两次、再积压不重放”的第六个队列回归；本轮最终新增 10 项测试（6 项队列、4 项播放列表）。

使用 PowerShell 7.6.5、.NET SDK 10.0.401，在 Windows x64 本机执行：

```powershell
# 修复前：新增前 5 项队列场景、原有溢出场景与播放列表回归。
dotnet test tests/MpvShell.Player.LibMpv.Tests/MpvShell.Player.LibMpv.Tests.csproj -p:Platform=x64 --no-restore --filter "FullyQualifiedName~BackendPlaylistRedirectTests|FullyQualifiedName~BackendBufferingEventTests.Overflow" --logger "trx;LogFileName=events-before.trx" --results-directory artifacts/p1-02-events
# 修复后定向验证使用完整 BackendBufferingEventTests，日志名 events-after.trx。
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p1-02-events/debug
dotnet build mpv-winui.slnx -p:Platform=x64 -c Release --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 -c Release --no-build --no-restore --logger trx --results-directory artifacts/p1-02-events/release
```

| 验证 | Debug | Release |
|---|---|---|
| 解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| Abstractions | 2 通过 | 2 通过 |
| LibMpv | 93 通过 | 93 通过 |
| App | 54 通过 | 54 通过 |
| Rendering.WinUI | 64 通过、5 跳过 | 64 通过、5 跳过 |
| 总计 | **213 通过、0 失败、5 跳过** | **213 通过、0 失败、5 跳过** |

`build-debug.log`、`build-release.log`、`test-debug.log`、`test-release.log` 及两个配置目录下的 TRX 保留实际输出。原有加载取消、恢复租约、HTTP/HLS、渲染恢复和应用终态回归均包含在本轮默认回归中。

五项跳过仍为 HEVC、AV1、PQ 渐变、TS seek 对照及自然播放性能测试，对应的 `MPVSHELL_TEST_*_MEDIA` 未配置；本轮没有新增这些媒体或硬件性能结论。新增 10 项全部实际运行。

独立只读复核检查了发布序号、补偿顺序、同一次观察中的去重及存储上限，未发现必修问题；跨订阅同步边界已列明。变更文本的 UTF-8 无 BOM、无替换字符、文档本地链接及 `git diff --check` 均通过。

## 5. P1-02 下一步

- `BackendFaulted` 仍只有消息文本，混合了媒体加载/播放错误、原生事件溢出与会话关闭。下一轮应明确错误类别和可恢复性，再补非渲染终态对迟到成功回复的保护。不能直接把所有媒体错误永久锁存。
- UI 事件仍逐条进入 DispatcherQueue；本轮修复后端积压时的状态恢复，UI 派发合并仍待处理。
- 对跨加载的 `StartFile` 归属尚未取得真实竞态复现，不把手工注入任意旧事件当作已证实的原生缺陷。上游属性事件存在合并机制，进一步修改需先建立真实可达时序证据。
- seek 竞争、完整状态/错误契约、页面生命周期及 PERF-02 仍按原工作包跟进。
