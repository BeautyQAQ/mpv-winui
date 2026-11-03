# P3-00 本机完整回归与故障诊断

> 日期：2026-11-03；本机完整四轮复验、实际候选 DLL 回归及最终内容核验均通过。
> 用户确认旧 940M / Intel 设备暂时不能访问，先完成本机验证。P3-00 整体保持待验证，旧设备原生崩溃/挂起不据本机结果关闭。

## 1. 范围与输入

本轮补齐 GTX 1060 / UU / Windows console 下的 Debug、Release 默认及显式素材回归，并核对测试与已完成 GUI 验收的候选身份。产品源码和项目配置未改；修复基线 runner 环境清理、最近记录测试缺少 UI 线程上下文、倍速测试提前结束等待三项验证问题，另增加可选挂起诊断参数。

| 项目 | 实际身份 |
|---|---|
| 基线提交 | `main / 18a85abe57d34abe3f59b45e623b50536a714a6a`；本轮工作区包含上轮 GUI 文档及本轮工具修改，不称为干净提交 |
| 候选 | `MpvShell-phase3-candidate-20261103-170050-5f9ed2`，来自上述干净提交，`sourceDirty=false`，290 份源码快照 |
| 主 ZIP SHA-256 | `050A3DBEC608F0DB27CD789D40C28333695DDEEA1CF99EB81EBE633194578861`；已登记 ZIP 不覆盖 |
| 环境 | Windows 10.0.26300，SDK 10.0.403，运行时 10.0.14，PowerShell 7.6.5；GTX 1060 5GB / 32.0.15.8266，另有 GameViewer 虚拟适配器；用户经 UU 连接，console 会话 9，无活动 RDP 会话 |
| 媒体 | 17 份固定输入、5 份清单；候选包内 15 份 HEVC/AV1/PQ/HLS/多轨/字幕/音频/视频样本，另加本机固定 30 秒 TS 和 SDR HEVC 4K60 性能素材 |
| TS 参数 | 顺序播放 20 秒；八个跳转目标 `2,4,7,11,16,21,25,9` 秒，均在 30 秒素材内 |
| 自然播放参数 | SDR、D3D11VA、要求真实硬解、3840×2160、SyncInterval=1、预热 3 秒、稳态 20 秒、不读回；未设置 FPS 判定阈值 |
| 原始证据根目录 | `artifacts/p3-00/closure-20261103-current-machine/`，下文路径均相对此目录 |

候选媒体及本机 TS/性能素材的生成参数、工具和 FFprobe 信息由各自清单固定，`media-inputs.json` 记录绝对路径及 SHA-256。它们不是 10 月 28 日另一台机器上缺席的编码文件，不能称作旧设备的相同输入复现。自然播放与测量自检的测试通过只证明断言满足和执行完成；没有吞吐判定条件的报告仍为“已测量，未判定”，不重写已获用户接受的 P3-01 / PERF-02 结果。

## 2. 首轮结果与环境清理缺陷

首轮使用独立目录 `full-baseline/`，执行过的 runner 副本、命令时间/参数/退出码、TRX 和完整日志均保留：

| 轮次 | 通过 / 失败 / 跳过 | 命令退出码 |
|---|---|---|
| Debug 默认 | 525 / 0 / 9 | 0 |
| Debug 显式 | 534 / 0 / 0 | 0 |
| Release 默认 | 524 / 1 / 9 | 1 |
| Release 显式 | 534 / 0 / 0 | 0 |

两配置构建均 0 警告、0 错误，188 项构建输入在本轮前后一致。首轮整体失败，不能用另三轮通过覆盖 Release 默认失败。

唯一失败为 `PlaybackPerformanceTests.Measurement_harness_should_run_on_generated_media_without_asserting_throughput`：`Directory.CreateDirectory` 在第 82 行收到空路径，抛出托管 `ArgumentException`，约 0.013 秒结束。`TestMode All` 从 Debug 显式切到 Release 默认时，旧 `Clear-TestEnvironment` 使用 `[Environment]::SetEnvironmentVariable($name, $null, 'Process')`，在本机 PowerShell 中留下空字符串；报告目录读取使用 `??`，空字符串没有触发缺省路径。九项条件测试使用空白判定仍跳过，因此只暴露为无条件测量自检失败。

本机最小复现 `environment-clear-reproduction.json` 确认：旧调用返回值是 `""`、变量仍存在；`Env:` provider 删除后返回真正的 null、条目消失。该行为与 [PowerShell 7.5 环境变量说明](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_environment_variables?view=powershell-7.5) 一致；.NET 对空字符串与真正 null 的区别见 [官方兼容性说明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/empty-env-variable)。

修复 runner，改为 `Remove-Item -LiteralPath ('Env:' + $variable.Name)`，随后检查无残留；保留调用方原有变量的收尾恢复。`verify-environment-clear.ps1` 直接提取实际 runner 函数，验证非空和空值两个条目都被删除、父进程无残留、子 PowerShell 不再继承条目且报告目录为 null，结果通过，见 `environment-clear-verification.json`。没有修改产品或 C# 测试来绕过失败。

runner 新增可选 `-HangTimeout 3m`，传递 `--blame --blame-hang-timeout 3m --blame-hang-dump-type mini`。单项超时按失败保存序列及挂起转储；不传参数保持原行为。3 分钟覆盖本次合法最长 TS 场景；这不是降低断言或允许失败重试，也不保证取得所有类型原生崩溃的 dump。

## 3. 修复后的完整复验

环境清理修复后使用新目录 `full-baseline-fixed/`，保留首次失败，不覆盖原始结果：

| 轮次 | 通过 / 失败 / 跳过 | 命令退出码 |
|---|---|---|
| Debug 默认 | 525 / 0 / 9 | 0 |
| Debug 显式 | 534 / 0 / 0 | 0 |
| Release 默认 | 525 / 0 / 9 | 0 |
| Release 显式 | 533 / 1 / 0 | 1 |

两配置构建仍均 0 警告/错误，188 项输入前后一致；环境清理缺陷已验证修复，但本轮整体失败。Release 显式失败位于 App 测试；渲染 79 项全部通过，不是原生崩溃或挂起。

### 3.1. 最近记录测试的并发刷新

`PlayerViewModelRecentHistoryTests.Clear_should_follow_an_accepted_save_and_precede_subsequent_open_history` 预期只保留一个 `new.mp4`，实际出现两份相同条目。存储写入的 first → empty → new 顺序仍正确。ViewModel 的历史完成续体依赖调用时的 UI 上下文；真实页面从 WinUI 线程调用，而旧测试直接在没有串行 UI 上下文的环境运行。清空与后续新增的完成续体可并发执行 `RefreshRecentUrls`，交错 Clear/Add 后产生重复。

`HistoryThreadProbe.cs` / `probe-history-threading.ps1` 对未修改的原测试 DLL 中同一个用例做有界对照：无线程上下文执行 200 次，199 通过、1 次同样的重复断言失败；串行 UI 上下文执行 200 次全部通过，见 `history-threading-probe.json`。这是一组诊断证据，不是替代全量回归的通过结果。

修正这组 11 个测试方法（18 项展开用例），使用 `SingleThreadTestContext` 模拟 WinUI 的串行异步上下文，不创建窗口；磁盘服务仍可通过后台续体完成。原保存顺序、迟到结果、去重、关闭等待等断言均保留，清空/随后打开用例新增上下文类型与所有集合更新线程一致性断言。未改产品代码，也没有给集合加锁来掩盖线程使用错误。

解决方案入口筛选最近记录组，Debug、Release 各 18 项通过、无失败/跳过，见 `history-context-validation/`。修正后同一方法再做两组各 200 次检查，400 次全部通过，见 `history-threading-fixed-probe.json`；该检查不替代真实 UI 或完整回归。

### 3.2. 第三次完整轮与倍速通知等待

两项验证修正后改用新目录 `full-baseline-final/`。每个配置先构建，再以 `--no-build --no-restore` 执行默认与显式两轮；默认轮实际删除全部 `MPVSHELL_TEST_*`，显式轮加载同一份已登记输入。

本轮 Debug 显式发现 `BackendStopAndSpeedTests.Late_media_events_after_stop_must_not_restore_media_eof_or_errors` 失败：用例等到 `Volume=39` 后，最后一条快照的 `Speed` 仍为 1，而断言要求 2。App 的 297 项及最近记录上下文修正均通过，失败位于 LibMpv 测试。

用例先等待设置 speed 的命令回复，再设置 volume，但它只把 volume 观察值当作所有属性观察值已经送达的屏障。仓库锁定的 mpv v0.41.0 [Client API 说明](https://github.com/mpv-player/mpv/blob/v0.41.0/include/mpv/client.h#L1127-L1183) 明确属性通知会合并、延后到事件队列空闲；据此不能推断另一属性的通知已先送达。修正为等待同一快照同时满足音量和倍速，另增加原生 speed 回读必须为 2.0 的断言；保留全部停止、迟到事件、错误/EOF、信息清空断言，不增加固定延迟、不改产品状态或 mpv 同步策略。

第三次完整轮最终结果如下；两次构建均 0 警告/错误，189 项构建输入前后一致，整体仍失败：

| 轮次 | 通过 / 失败 / 跳过 | 命令退出码 |
|---|---|---|
| Debug 默认 | 525 / 0 / 9 | 0 |
| Debug 显式 | 533 / 1 / 0 | 1 |
| Release 默认 | 525 / 0 / 9 | 0 |
| Release 显式 | 534 / 0 / 0 | 0 |

三次完整轮的失败原样保留；旧设备未完成的五项在本机这些显式轮均已有完成结果，不能据此将整轮失败写成通过。

### 3.3. 最终复验

三项验证修正后，使用独立目录 `full-baseline-verified/` 再执行完整四轮，命令均退出 0：

| 轮次 | 通过 / 失败 / 跳过 | TRX 份数 |
|---|---|---|
| Debug 默认 | 525 / 0 / 9 | 4 |
| Debug 显式 | 534 / 0 / 0 | 4 |
| Release 默认 | 525 / 0 / 9 | 4 |
| Release 显式 | 534 / 0 / 0 | 4 |

Debug/Release 构建各 0 警告、0 错误；`closure-verification.json` 确认 6/6 条构建/测试命令完成且全部通过、189 项构建输入前后一致。`media-validation-before.json` / `media-validation-after.json` 的 17 份素材及 5 份清单均匹配。

HEVC 报告确认 D3D11VA / GPU resident、非黑画面；AV1 实际为允许的软件回退，不能把用例名中的 hardware 泛化成 AV1 硬解通过。自然播放报告保持“已测量，未判定”，少量呈现丢帧仍保留在原始报告，不把功能断言通过写成新的吞吐达标结论。

默认九项条件跳过为 HEVC、AV1、PQ、TS、自定义媒体自然播放及四项 HLS/字幕集成测试，显式轮必须全部实际完成。测量 harness 自检不是条件跳过项。统计以 `UnitTestResult outcome` 为准，不使用 TRX 的 `Counters.notExecuted` 代替。

## 4. 测试与实际候选二进制的对应

`candidate-build-input-comparison.json` 先核对前两次回归的 188 项构建输入与候选 290 文件源码快照：当时 187 项完全一致，唯一差异为 `mpv-winui.slnx` 后补的 GUI 文档条目。最终 `candidate-build-input-comparison-final.json` 检查 189 项，其中 185 项匹配，差异为解决方案文档条目、两个修正的测试文件和一个新增测试上下文 helper；113 项产品构建输入全部与候选快照匹配。去除文档 `File` 节点后，解决方案项目及配置完全一致。不能声称全部测试源与候选快照相同；产品源码、原生依赖和构建配置不变。

普通 Release 构建的 App / Rendering DLL 与自包含候选对应 DLL 哈希不同，不能将它们写成相同二进制；Abstractions / LibMpv DLL 相同。候选 GUI 的原始哈希见 [最终目录 GUI 验收](P3-00-02-candidate-final-directory-gui.md)。为覆盖实际交付二进制，使用 `test-candidate-binaries-via-solution.ps1` 先将四个 Release 测试输出中待替换的产品 DLL、PDB 和原生布局文件备份到证据目录，再临时换入候选中的原文件。仍从解决方案入口以 `--no-build --no-restore` 运行，测试前后核对候选复制来源及哈希，结束后恢复原测试输出并逐项核验；原候选目录和 ZIP 不修改。

该补验实际完成 **534 通过、0 失败、0 跳过，4 份 TRX，命令退出 0**，534 个测试名称与最终四轮逐项一致；见 `candidate-binary-explicit/summary.json`、`commands.json`、`trx/`。临时替换的 35 份 DLL/PDB/原生布局文件测试前后均保持候选哈希，测试结束后全部恢复原哈希，见 `binary-identities-{before,after}.json` 与 `test-output-restoration.json`。测试主机使用共享 .NET 10.0.14，仍是 VSTest 的功能与渲染验证；自包含应用启动与真实 GUI 由原候选最终目录的三轮记录覆盖，正常退出均为 0，不把两种宿主写成同一次运行。

## 5. 旧设备故障与剩余边界

10 月 28 日 940M / Intel HD 4600 设备的两个显式轮各记录 529 项后发生原生 `0xC0000005`，独立 Debug 自然播放随后两分钟无活动超时。旧失败、缺失的五项结果及当时无 dump 的限制继续保留于 [原始记录](P3-00-01-baseline-and-candidate.md#31-事件日志与单项诊断)。

本机首轮、最终完整轮两个显式配置及实际候选 DLL 均实际完成 534 项；旧轮缺失的 HEVC、AV1、PQ、harness 自检和自然播放，在最终 Debug/Release 中的十份逐项结果均为 Passed，见 `previously-missing-five-results.json`。未出现同一原生崩溃或挂起。两台设备、驱动、显示路径和编码文件存在差异，因此只能登记“本机未复现”。本轮三项修正针对验证工具与测试调度，不是旧 `0xC0000005` 的根因或修复证据。

用户已确认旧设备暂时无法访问，先完成本机验证。本轮不修改驱动、渲染同步策略或产品生命周期代码来猜测修复。旧设备恢复后，应使用其原二进制/媒体及已登记参数，先复现并取得可分析的崩溃或挂起转储，确认实际适配器、渲染线程与原生调用状态，再决定修复并在同条件下复验。

## 6. 当前结论

本机完整回归、实际候选 DLL 补验和最终目录 GUI 已全部补齐。`final-verification.json` 的 `currentMachineChecksPassed=true`：188 项编译输入在最后完整轮后未变，113 项产品输入匹配原候选，22 处媒体/清单哈希匹配；主 ZIP 693 个文件及源码 ZIP 290 个文件逐条名称、长度、SHA-256 全部匹配，候选目录和主 ZIP/源码 ZIP 哈希均未变。

用户最近记录 SHA-256 仍为 `024E43BF323BD590229148F46E831F64EB114AAC28F44E8F6C9CA7C0932A408F`，无播放器或 testhost 残留。13 份变更文本的 UTF-8 无 BOM 检查、解决方案解析和 `git diff --check` 通过。

独立收尾核验脚本首次遗漏主 ZIP 的包名目录前缀，造成条目查找失败；修正为与封包器一致的前缀后，逐项内容核验全部通过。初次核验脚本和报告保存在 `verification-driver-first-attempt/`，属于核验驱动错误，不是产品或 ZIP 内容变更。

包外 [回归补充清单](../../../artifacts/candidates/MpvShell-phase3-candidate-20261103-170050-5f9ed2.regression-20261103.json) 关联主 ZIP/源码哈希、四轮和实际候选 DLL 结果、源码关系及证据哈希清单 `evidence-sha256.json`；原包内说明及源码快照保留封包时状态，不改写已登记 ZIP。前三次失败、两种测试修正前后的对照和最后通过结果均保留。

**P3-00 整体保持待验证**，剩余旧 940M / Intel 原生崩溃/挂起的原机诊断。P3-02/P3-03 仍未开始，Phase 3 未完成；本机通过不扩展其他设备、长期、听音、触控或 HDR 仪器验收范围。
