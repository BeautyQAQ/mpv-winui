# P1-02：加载取消与提交边界

- 日期：2026-10-13。
- 输入提交：`4fcaef4`；本记录对应其上的工作区修改。
- 范围：连续加载、恢复锁前排队取消、已提交加载的取消/超时等待。P1-02 仍在进行中，不代表全包验收。
- 原始记录：`artifacts/p1-02-audit/`；本轮未发布新包。

## 1. 实际复现

使用本机随机端口 HTTP 服务生成 PCM WAV，并通过显式响应屏障固定加载时序。恢复租约阻止后续播放修改提交，不依赖任意休眠制造竞态。

修复前运行 `BackendLoadCancellationTests` 的前三个场景，结果 **1 通过、2 失败**，见 `load-before.trx`：

1. A 已加载；恢复租约期间请求 B，在 B 尚未提交时取消。原生 `path` 仍为 A，后端 `CurrentUrl` 却变成 B。
2. A 的 HTTP 响应被暂缓；取消 A 的等待，恢复租约期间请求 B；释放 A 的响应。B 尚未提交就设置 `_awaitingStart`，导致 A 的真实 `FileLoaded` 被丢弃。随后取消 B、释放租约，调用播放得到“请先打开媒体”。
3. A 已开始读取 HTTP，但调用方取消等待；提交并加载时长不同的 B。此对照在修复前已通过，保留以确认本轮未破坏正常替换。

## 2. 契约与修复

| 边界 | 本轮明确的行为 |
|---|---|
| 等待加载锁、恢复锁或命令队列，尚未提交 `loadfile` | 不以新地址覆盖当前媒体，不进入等待新 `StartFile` 的状态；取消后原媒体事件继续生效 |
| 原生入口接受 `loadfile` 请求 | 在 mpv 事件/命令线程内同步建立加载等待与当前地址，再派发后续媒体事件；接受请求不等于媒体已成功加载 |
| 已提交后取消或等待超时 | 仅结束调用方等待，不发送 stop 或宣称撤销加载；实际媒体事件仍继续更新状态 |
| 加载完成 | `FileLoaded` 完成调用方等待；`loadfile` 命令回复本身不是加载成功证据 |
| 提交回调与取消同时发生 | 在 `_stateGate` 下协调等待者是否仍存在，取消先完成时不重新登记已经离开的等待者 |

`LibMpvBackend.LoadUrlAsync` 原先在等待暂停请求、恢复锁之前修改媒体状态。现在由 `MpvPlayerSession` 在原生入口返回成功后调用内部提交通知，再修改状态。加载方法的 `finally` 只解除自己的等待关联，不清除仍属于原生加载过程的 `_awaitingStart`。

没有新增原生入口或改变 ABI。已核对上游 [v0.41.0 Client API](https://github.com/mpv-player/mpv/blob/v0.41.0/include/mpv/client.h) 的异步请求/回复语义，以及 [loadfile 文档](https://github.com/mpv-player/mpv/blob/v0.41.0/DOCS/man/input.rst)：命令返回与媒体实际加载是两个阶段。本轮继续使用固定原生 DLL。

## 3. 验证

首次修复后上述三个场景 **3 通过、0 失败、0 跳过**，见 `load-after.trx`。随后补充真实 20 秒加载等待超时场景：仅将测试会话的网络超时设为 30 秒，待后端超时返回后释放 HTTP 响应，确认媒体能完成加载并继续暂停/播放。

最终使用 PowerShell 7.6.5、.NET SDK 10.0.401，在 Windows x64 本机执行：

```powershell
dotnet test tests/MpvShell.Player.LibMpv.Tests/MpvShell.Player.LibMpv.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~BackendLoadCancellationTests --logger "trx;LogFileName=load-before.trx" --results-directory artifacts/p1-02-audit
# 修复后，前三个场景使用相同筛选条件，日志名改为 load-after.trx。
# 加入第四个超时场景后，执行以下完整回归。
dotnet build mpv-winui.slnx -p:Platform=x64 -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p1-02-audit/debug
dotnet build mpv-winui.slnx -p:Platform=x64 -c Release --no-restore -v:minimal
dotnet test mpv-winui.slnx -p:Platform=x64 -c Release --no-build --no-restore --logger trx --results-directory artifacts/p1-02-audit/release
```

| 验证 | Debug | Release |
|---|---|---|
| 解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| Abstractions | 2 通过 | 2 通过 |
| LibMpv（含新增 4 项） | 83 通过 | 83 通过 |
| App | 54 通过 | 54 通过 |
| Rendering.WinUI | 64 通过、5 跳过 | 64 通过、5 跳过 |
| 总计 | **203 通过、0 失败、5 跳过** | **203 通过、0 失败、5 跳过** |

日志为 `build-debug.log`、`build-release.log`、`test-debug.log`、`test-release.log`，TRX 分别保存在 `debug/`、`release/`。新增四项没有跳过，取消与超时后使用同一真实会话继续播放；HTTP 服务仅绑定本机随机端口，样本由测试生成。

五项跳过分别为 HEVC、AV1、PQ 渐变、TS seek 对照和自然播放性能场景；本轮未设置对应 `MPVSHELL_TEST_HEVC_MEDIA`、`MPVSHELL_TEST_AV1_MEDIA`、`MPVSHELL_TEST_HDR_GRADIENT_MEDIA`、`MPVSHELL_TEST_TS_MEDIA`、`MPVSHELL_TEST_PERF_MEDIA`。没有将默认回归解释为这些媒体或性能场景的新验收。

变更文本通过 UTF-8 无 BOM、无替换字符及文档相对路径检查，`git diff --check` 通过。解决方案文件、原生依赖和发布入口未修改。

## 4. 后续审计边界

- 本轮证据覆盖所列本机 WAV 与恢复锁时序，不宣称解决所有 StartFile/属性通知交错；跨命令的媒体身份关联仍需继续审计。
- 取消可能发生在解除暂停已提交、`loadfile` 尚未提交之间；本轮保证媒体地址与加载事件不提前切换，不承诺回滚已经提交的暂停修改。
- seek 竞争、请求提交后取消/超时的完整组合、错误分类与不可恢复终态、关键事件在积压时的保留和 UI 派发合并，仍属于 P1-02 后续工作。
- PERF-02、页面生命周期、视频 HLS/字幕与最终发布继续按各自工作包推进；没有新增硬件/性能验收结论。
