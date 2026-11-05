# P3-00 RTX 3070 本机复测

> 状态：P3-00 在本轮声明的 RTX 3070 范围内通过；两处工具缺陷修复后采用的默认/显式回归、两份视频 EOF、最终候选 GUI 与封包核验完成。首次失败保留，Phase 3 尚未完成。
> 日期：2026-10-29
> 对应计划：[Phase 3](../phase-3-plan.md)
> 历史失败：[2026-10-28 基线与候选记录](P3-00-01-baseline-and-candidate.md)
> 原始证据：`artifacts/p3-00-retest-20261029/`

## 1. 范围与身份

本轮根据用户授权，使用 `C:\Users\a1426\Downloads\video-test` 中的两份视频及仓库生成器的合成素材，推进当前版本回归、原生故障复现、短时稳定性和候选目录验收。开工源码为干净的 `main / 4df49f5214116d655a3ac096ce566b4a707d89f5`。下文初始默认/显式四轮、生命周期和采样对照均先在这一未修改源码上完成；随后真实 EOF 探针暴露可选诊断属性读取缺陷，才最小修改 `AppRecoveryProbe.cs`，再修正 All 模式暴露的 `test-candidate-baseline.ps1` 环境清理缺陷。原生 DLL 和测试断言未改，修复前后证据分别保留。

本机实际渲染设备为 RTX 3070，与 2026-10-28 记录的 940M/Intel 环境不同。本机通过只对应本轮环境与输入；940M/Intel 历史崩溃及关联挂起跟进已于 2026-10-29 按用户要求标记为已完成并关闭，原始失败记录继续保留。工作开始时，旧候选 ZIP、源码快照和原始 P3-00 报告不在当前检出中，本轮重新建立独立素材与证据，不声称重新核验旧产物。

| 项目 | 本轮实际值 |
|---|---|
| 系统与工具 | Windows 10.0.26300；.NET SDK 10.0.403；PowerShell 7.6.5 |
| GPU 与驱动 | NVIDIA GeForce RTX 3070；驱动 `32.0.16.1742`；系统另列出两个虚拟显示适配器，实际设备以各场景报告为准 |
| 初始构建输入 | 默认及显式各 188 项；轮内前后不变，两轮清单逐项一致，初始回归结束后重算无差异 |
| 修复后构建输入 | `final-baseline/` 与 `final-default-env-retest/` 各 188 项，逐项一致；Debug 11 份及 Release 14 份记录的二进制逐项相同 |
| 原生 libmpv | SHA-256 `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705`，Client API 2.5 |
| 初始候选 | `MpvShell-phase3-candidate-20261029-192208-33265b`，Release / `win-x64`，.NET 与 WinUI 自包含；保留为 EOF 诊断修复前产物，不作最终候选 |
| 初始候选源码 | 构建时 `sourceDirty=false`，干净的 `4df49f5`；288 文件源码快照 |

初始候选快照为干净 HEAD，本记录不在该快照中。修复后新候选 `MpvShell-phase3-candidate-20261029-193719-e7d814` 的快照包含两处修复和构建时尚在进行中的本记录，`sourceDirty=true`；后补的最终结果不在这一冻结快照中。两份源码 ZIP 均保留原始身份，不能仅以基线提交或后来补齐的文档代替实际构建来源。

## 2. 素材与诊断工具

`media-inputs.json` 冻结 18 份合成输入、5 份清单和 18 个测试变量。显式回归前后各 23 项哈希全部匹配。素材为本轮重新编码，不能混用历史编码文件的哈希。

| 输入 | 生成及核验 |
|---|---|
| `media-hdr/` | 4K30 HEVC Main10 HDR10、4K30 AV1 10-bit SDR、4K FFV1 PQ 渐变；哈希、元数据及全片 CPU 解码通过，FFV1 首帧与原始 10-bit 图案逐字节一致，渐变含 660 个不同码值 |
| `media-p1-04/` | 12 份 HLS、多音轨、SRT/ASS、纯音频与纯视频输入；使用既有生成脚本和清单 |
| `media-ts/` | 720p60、30 秒开放 GOP HEVC TS；17 个片长内跳转目标和 20 秒顺播，未使用超出合成片长的默认目标 |
| `media-perf/` | 4K60、30 秒 SDR HEVC 与 HDR10 Main10 对照；显式全量使用 SDR 输入、保留 D3D11VA GPU resident 要求，不设置吞吐通过阈值 |
| 用户 LG TS | HEVC Main10、3840×2160、60000/1001 fps、PQ/BT.2020，容器时长约 132.387 秒；SHA-256 `0E9E323A579A7064DB338C7EB34EC184BE0BD25B811FD021EFEE4A7320BEF6C3` |
| 用户洞穴 MP4 | HEVC Main10、3840×2160、24000/1001 fps、PQ/BT.2020，容器时长约 94.123 秒；SHA-256 `D1746C88716951EC1180EC3A1190900CD79F80296BB0C2387EFBDBF76B906851` |

FFmpeg/FFprobe 使用仓库历史登记的 Gyan FFmpeg 8.1.2 essentials 构建，下载归档与站方校验和及仓库历史值一致：`E25B682664025D49034C981AFB4BAE36238A40F29A3CC1C713AD9A8B5B3528F6`。具体来源、工具文件哈希和编码参数分别保留在 `tools/tool-provenance.json` 与各素材清单。所有编码完成后才执行正式 S4/S5 对照，各测量串行运行。

ProcDump 从 Microsoft Sysinternals 下载，`procdump.exe` 与 `procdump64.exe` 的 Authenticode 签名均有效、签名者为 Microsoft Corporation。独立诊断设置 `PROCDUMP_PATH`、`VSTEST_DUMP_FORCEPROCDUMP=1`，同时启用 `--blame-crash` 与两分钟挂起 full dump；工具哈希见 `tools/procdump/provenance.json`。正常完成未产生崩溃或挂起转储，不将无 dump 解读为已定位历史故障。

用户视频只作为本机输入，未修改，也不进入候选分发素材。分发素材仅使用仓库生成器生成的文件。

## 3. 构建与完整回归

### 3.1. 未修改源码的初始四轮

使用 `build/testing/test-candidate-baseline.ps1`，以解决方案入口执行 x64 Debug/Release 构建及 `--no-build --no-restore -m:1` 回归。默认轮清理 `MPVSHELL_TEST_*`，显式轮加载固定输入清单，结束后恢复调用环境。

| 配置与轮次 | 构建 | 测试结果 | 原始证据 |
|---|---|---|---|
| Debug 默认 | 0 警告、0 错误 | 525 通过、0 失败、9 跳过，退出 0 | `default-baseline/Debug-{build,default}.log` 及 4 份 TRX |
| Release 默认 | 0 警告、0 错误 | 525 通过、0 失败、9 跳过，退出 0 | `default-baseline/Release-{build,default}.log` 及 4 份 TRX |
| Debug 显式 | 0 警告、0 错误 | 534 通过、0 失败、0 跳过，退出 0 | `explicit-baseline/Debug-{build,explicit}.log` 及 4 份 TRX |
| Release 显式 | 0 警告、0 错误 | 534 通过、0 失败、0 跳过，退出 0 | `explicit-baseline/Release-{build,explicit}.log` 及 4 份 TRX |

16 份 TRX 已逐项核对，两类回归均无其他状态结果。两份 `closure-verification.json` 均为 `allCommandsPassed=true`、完成命令 4/4、`buildInputsUnchanged=true`。显式回归实际完成了上轮缺少结果的 HEVC、AV1、PQ、性能链路自检和自然播放性能用例。

本轮完整回归未发生原生崩溃或挂起；没有通过放宽断言、取消硬解要求、跳过条件测试或重跑覆盖首轮失败来取得上述结果。该结论仅属于本机、当前输入和源码；940M 历史故障跟进已按用户要求完成并关闭。

### 3.2. 修复后 All 模式的工具失败

EOF 探针修复后，使用 `TestMode All` 在 `final-baseline/` 执行默认及显式四轮。Debug 默认为 525/0/9，Debug 显式为 534/0/0；Release 默认为 **524 通过、1 失败、9 跳过，退出 1**，之后 Release 显式仍完成 534/0/0。四次计数均保留实际 TRX，`closure-verification.json` 保持 `allCommandsPassed=false`、完成命令 6/6，不能因后续复验通过而改写。

Release 默认失败项为性能链路自检，约 6 ms 即在 `Directory.CreateDirectory("")` 抛 `ArgumentException`。真实负例 `runner-env-validation.json` 证明：在本机 PowerShell 7.6.5 中，原清理方法 `[Environment]::SetEnvironmentVariable(name, $null, 'Process')` 实际将变量保留为空字符串（`isNull=false`、`present=true`），Debug 显式轮的 `MPVSHELL_TEST_HARDWARE_REPORT_DIR` 因此进入下一配置默认轮。测试读取空字符串未使用默认目录，与渲染吞吐或原生崩溃无关。

最小修复仅将 `Clear-TestEnvironment` 改为对匹配的 `MPVSHELL_TEST_*` 使用 `Remove-Item -LiteralPath ('Env:' + name)`。同一真实负例对照中，修复函数得到 `value=null`、`isNull=true`、`present=false`，无关环境变量保留；未改产品测试或吞吐断言。验证记录同时保存实际函数文本与 runner SHA-256。

### 3.3. 最终采用的回归结果与身份核对

修复 runner 后，在同一 PowerShell 调用中先加载素材清单全部 18 个变量并加入两个显式报告目录，再运行 `TestMode Default`。预种条件记录于 `default-env-transition-seed.json`，两配置默认均通过，证明从已有显式环境进入默认轮时，修复后的清理函数实际移除了这些变量。

| 最终采用项 | 结果 | 证据 |
|---|---|---|
| Debug 默认复验 | 525 通过、0 失败、9 跳过，退出 0 | `final-default-env-retest/Debug-default/`，4 份 TRX |
| Release 默认复验 | 525 通过、0 失败、9 跳过，退出 0 | `final-default-env-retest/Release-default/`，4 份 TRX |
| Debug 显式 | 534 通过、0 失败、0 跳过，退出 0 | `final-baseline/Debug-explicit/`，4 份 TRX |
| Release 显式 | 534 通过、0 失败、0 跳过，退出 0 | `final-baseline/Release-explicit/`，4 份 TRX |

`final-default-env-retest/closure-verification.json` 为 `allCommandsPassed=true`、完成命令 4/4。两轮共 188 项编译输入逐项一致，Debug 11 份、Release 14 份记录的二进制路径和 SHA-256 也逐项相同；当前重新计算输入无差异，核对结论见 `final-regression-selection.json`。因此保留修复后已经通过的两轮显式结果，针对环境切换问题补做两配置默认复验，不重复与 runner 清理修复无关的显式全量。这里采用的是不同轮次中身份一致的有效结果，不能表述为原 All 轮通过或本轮四轮首次通过。

上述二进制一致专指所采用两轮已记录的文件互相比对；回归的非 RID Rendering 输出/测试副本与候选的 `win-x64` 输出分别保留身份，候选按自身发布、源码快照、DLL 哈希与最终 GUI 进程验收。`final-input-integrity.json` 另确认两处修复与候选源快照一致，两份用户视频哈希未变。

## 4. 独立用户 TS 诊断

在 Release 下单独筛选自然播放性能用例，使用用户 LG TS，预热 3 秒、稳态 20 秒、3840×2160 SDR BGRA8 离屏表面、`d3d11va`、要求 GPU resident、vsync 1、窗口内无 CPU 读回。命令退出 0，实际完成 1 项功能用例，耗时约 24 秒。

报告确认 RTX 3070、`d3d11va / d3d11-egl / d3d11`、10-bit `p010`，稳态 1,199 帧、约 59.912 fps，窗口内 VO 和解码丢帧差均为 0，结束后观察到非黑画面。全片累计呈现丢帧为 2，不能把窗口内零差值写成全程零丢帧。

证据为 `isolated-release-user-ts/{command,result}.json`、`test.log`、TRX 与 `hardware/performance-isolated-release-user-ts.json`。此轮与 CPU 素材准备时段重叠，仅用于复现与功能诊断；未设置吞吐阈值，报告状态为“已测量，未判定”，不将其作为正式性能验收或显示器流畅度结论。

## 5. S4/S5 有采样与无采样对照

执行前在 `test-plan.json` 和 `perf-pairs-plan.json` 固定条件，沿用既有 R1：稳态 fps ≥ 30×0.98＝29.4、VO 丢帧不超过窗口帧数的 1%、解码丢帧为 0。四项均为同一 Debug 构建、实际启用 D3D11 调试层、3840×2160 SDR BGRA8 离屏表面、vsync 1、预热 2 秒及稳态 5 秒；每对只改变 CPU 读回间隔（0 与 0.1 秒），实际媒体哈希一致。

S4 使用 HEVC HDR10 并要求 D3D11VA GPU resident。S5 显式设置 `hwdec=no` 测 AV1 软件路径，实际报告为 Software；这是强制软件解码对照，不能写成硬件不可用时的自动回退验证。

| 场景 | 读回 | 实际 fps | 呈现帧 | 读回次数 | VO/解码丢帧差 | Render p95（ms） | R1 |
|---|---|---:|---:|---:|---|---:|---|
| S4 HEVC HDR10 | 无 | 29.993 | 151 | 0 | 0 / 0 | 2.6624 | 通过 |
| S4 HEVC HDR10 | 0.1 秒 | 30.122 | 151 | 37 | 0 / 0 | 8.4760 | 通过 |
| S5 AV1 软件 | 无 | 29.976 | 151 | 0 | 0 / 0 | 3.3635 | 通过 |
| S5 AV1 软件 | 0.1 秒 | 30.093 | 151 | 37 | 0 / 0 | 8.9818 | 通过 |

四个命令均退出 0，各实际完成 1 项功能用例，原始报告及结果位于 `perf-S4-*`、`perf-S5-*`；汇总为 `perf-pairs-summary.json`。测试本身未设置 `MinimumPresentedFps`，其 JSON 仍为“已测量，未判定”；上表的 R1 判定来自执行前登记的规则与实际计数，不能只凭测试绿灯作出。

本机两对均满足 R1，有采样组的 Render p95 较高，但未出现同条件吞吐未达标。不能用本机结果解释历史硬件上的 105/110 丢帧，也不据此证明旧掉帧只由采样引起；PERF-02 的历史归因保持独立。离屏表面结果不替代真实应用窗口、显示器输出或其他显卡条件。

## 6. 生命周期与短时稳定性

执行前规定 11 项场景各运行一次，每个独立应用进程限时 90 秒；通过条件包括报告 `Status=Passed`、退出码 0、`ShutdownCompleted`、`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe`，以及日志中的会话销毁和图形资源按序释放标记。

`test-app-recovery.ps1 -NoBuild` 使用本轮 Debug 应用及脚本生成的运动图案，实际 11 项全部通过：播放恢复、暂停恢复、重建后首帧再次失败、设备重建失败、resize、播放/暂停页面卸载重挂、恢复中关闭、加载中关闭、启动加载中关闭、恢复租约持有时换媒体。原始逐项报告、会话日志及汇总保留在 `lifecycle/`。

启动加载关闭场景确认真实启动 URL 仍待完成，关闭耗时约 0.101 秒、加载等待结束约 0.103 秒，共用 5 秒预算；关闭后相关资源释放及重复关闭安全均为 true。这些注入探针不触发真实驱动重置，有限的一轮场景不代表长时间稳定性或资源趋势验收。

### 6.1. 用户媒体首次 EOF 探针失败与最小修复

两份用户媒体的首次 `playback` 场景均使用上述修复前 Debug 二进制、240 秒进程上限。LG TS 实际运行约 134.367 秒，洞穴 MP4 约 95.402 秒；两轮应用进程退出码均为 0，且 `ShutdownCompleted`、`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe` 均为 true，但报告 `Status=Failed`，驱动脚本据此失败。进程正常退出不能覆盖报告失败。

两轮异常均为 `ReadMpvCountersAsync` 在片尾统计时直接读取 `hwdec-current`，收到 `MPV_ERROR_PROPERTY_UNAVAILABLE(-10)`；调用栈经 `VerifyPlaybackToEndAsync` 的结束计数读取。LG TS 日志明确记录实际 EOF 和图形资源按序释放，报告也保留了多个时刻的变化图像；但后续完整播放断言因统计异常未执行完，不能将首次场景记作通过。原始失败分别保留于 `real-playback-ts/`、`real-playback-mp4/` 及同名 runner 日志。

本轮随后仅将这处可选字符串统计读取改为：遇到 `-10` 返回 null，其他请求错误继续传播。未改播放期间解码模式检查、图像变化、EOF、缓冲或资源释放断言。该缺陷属于 Debug 验收探针的片尾统计处理，不是上轮原生访问冲突的根因。修复后回归结果见 §3.2～3.3，真实 EOF 复验见下一节。

### 6.2. 修复后两份用户媒体完整 EOF 复验

使用修复后固定的 Debug 应用及同一原始用户视频重新执行 `playback`，每进程上限仍为 240 秒，先前失败目录未覆盖。`final-real-playback-result.json` 确认两个驱动脚本均退出 0；两目录逐项报告、汇总与会话日志均已核对。

| 输入 | 场景总耗时 | 实际片尾 / 时长（秒） | 变化图像样本 | 记录的呈现 fps | 报告 / 进程 |
|---|---:|---|---:|---:|---|
| LG 4K60 HDR TS | 133.708 秒 | 132.365622 / 132.365622 | 6 | 59.910 | Passed / 0 |
| 洞穴 4K24 HDR MP4 | 95.356 秒 | 94.122667 / 94.122667 | 6 | 23.953 | Passed / 0 |

两份报告均确认 `ShutdownCompleted`、`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe=true`，没有页面错误；多个间隔采样画面变化、播放期间解码模式不变以及片尾/缓冲断言全部执行并通过。实际窗口输出为 HDR10、10-bit PQ / BT.2020，Windows HDR 已开启；表面为 1658×1084，不能写成此轮完整播放使用了 4K 全屏表面。

EOF 后解码属性及部分 mpv 计数实际不可用，结束计数保留 null，没有补成 0；因此上述 fps 仅为这一段带间隔采样播放的记录，不作全程零丢帧或 R1 性能通过判定。首轮失败与修复后通过分别保留在 `real-playback-{ts,mp4}/` 和 `final-real-playback-{ts,mp4}/`。两段约 132 秒与 94 秒的视频各播放一次，仍属于短时功能和稳定性证据，不代表长时间运行或资源持续增长已验收。

## 7. 候选目录与来源

### 7.1. 修复前候选

初始候选目录为 `artifacts/candidates/MpvShell-phase3-candidate-20261029-192208-33265b/`，在发现 EOF 探针缺陷前生成。`publish-candidate.ps1` 已完成 Release 自包含发布、四个原生 DLL 的锁定哈希/AMD64/实际加载核验、临时及最终目录各 3 次会话创建初始化销毁，并复制 26 个依赖包的许可、通知及元数据。该目录保留原身份，不因修复后复验而重新解释为修复后产物；本轮最终候选须另行生成。

| 项目 | 当前身份 |
|---|---|
| 源码 ZIP | `MpvShell-phase3-candidate-20261029-192208-33265b-source.zip`，288 文件；登记 SHA-256 `3FB4A2B291A7338B879F522709BF4C2D632E80F8EABCDD67D2546E6403A47D9A` |
| 源码清单 SHA-256 | `CB3C23571AFD96A7DE1315FA9D351ECCE7DDA8389AC62CE8E08964A2567049CF` |
| 应用 DLL SHA-256 | `057F97074D9C374BAA9D722F2B4D0C83E232E3DCFB664CF83386EF91A0B17B25` |
| 托管 LibMpv DLL SHA-256 | `784D3AA2156C66A9DAB8BA4594A6CEF7E60D7A950B8C77A180A7A4B9E4B1D25C` |
| 素材 | 包内登记 15 份合成功能素材；不含用户视频及较大的 4K60 性能输入 |
| 当前限制 | 未作最终目录 GUI 或主 ZIP 验收；因 EOF 探针修复另行生成最终候选，不能用本目录身份代替 |

`gui-preflight/` 只证明本次可绑定开发输出目录的播放器窗口并通过 UI 正常关闭，不能替代上述最终候选目录验收。本轮开始时用户历史文件不存在；最终验收后须恢复这一状态并记录清理结果，不把测试媒体保留在用户历史中。

### 7.2. 修复后新候选

`artifacts/candidates/MpvShell-phase3-candidate-20261029-193719-e7d814/` 已按修复后源码另行发布，临时与最终目录的原生加载及会话烟雾通过；未覆盖修复前目录。候选源快照包含 `AppRecoveryProbe.cs`、runner 清理修复和当时进行中的本记录，`sourceDirty=true`，精确来源以快照和清单为准。

| 项目 | 新候选身份 |
|---|---|
| 基线与配置 | `4df49f5214116d655a3ac096ce566b4a707d89f5` 加登记的工作区修改；Release / `win-x64` 自包含 |
| 源码 ZIP | `MpvShell-phase3-candidate-20261029-193719-e7d814-source.zip`，289 文件；登记 SHA-256 `F2758F6955145193D6D676F850726FB9C1F714B863AB5445529D38D28E095193` |
| 源码清单 SHA-256 | `25DAECA912FBEAE96A1EF909DCFF730B52930BAB187C3264A6E40B58BDA5E347` |
| 应用 DLL SHA-256 | `9F5B643E3C82B978930EA4BACB8C795C4D9270C2D05C9730B9E00F28649C2889` |
| 托管 LibMpv DLL SHA-256 | `784D3AA2156C66A9DAB8BA4594A6CEF7E60D7A950B8C77A180A7A4B9E4B1D25C` |
| 当前结论 | 修复后两份用户媒体 EOF、最终目录 GUI/两次正常退出/重启恢复及主 ZIP 封包核验通过；仅限本轮声明范围 |

后续补写文档不修改已冻结的源码 ZIP，也不把它与当前工作区逐字节相同混为一谈。新候选附带的测试媒体仅为合成功能素材，私人 LG TS 和洞穴 MP4 不进入分发目录。

### 7.3. 最终 Release 目录 GUI 与正常退出

使用 Computer Use 从最终候选目录启动 Release 应用，进程记录和 DLL 哈希均与 §7.2 相同。`gui-final/summary.json` 登记 8 项通过；实际视频画面由执行端直接观察工具返回的截图，截图未另存磁盘，目录中的逐步 JSON 为窗口与 UIA 状态记录，不能把这些 JSON 称作截图文件。当前环境为 RTX 3070、Windows HDR 开启、4K 显示器及 150% DPI；不据此判定 HDR 色准或其他硬件。

| 场景 | 已核对结果 | `gui-final/` 原始证据 |
|---|---|---|
| 实际画面、播放/暂停/恢复 | 执行端看到两段媒体画面；UI 播放按钮及播放状态往返，进度继续 | `24-replay-running.json`、`28-pause-confirmed.json`、`29-resumed.json`、`32-media-switch-confirmed.json`、`42-history-playback-visible.json` 与会话日志 |
| 停止后重播 | 已停止时进度/时长归零、停止按钮禁用，随后重新打开同一 TS 并播放 | `22-stopped-observed.json`、`24-replay-running.json`、`logs-run1/` |
| 1.5× 倍速 | 执行端看到 1.5×，日志 `Set property: speed=1.500000 -> 1` | `17-speed-1p5.json`、`20-rebound-candidate.json`、`logs-run1/` |
| 播放中换片 | TS 切换到洞穴 MP4，时长变为 01:34、实际播放与画面确认 | `30-other-media-entered.json`、`32-media-switch-confirmed.json` |
| F11 / Esc | 控件由“全屏”变为“退出全屏”且标题栏消失，返回窗口后标题栏恢复；日志确认实际 3840×2160 表面后回到 1658×1084 | `33-fullscreen.json`、`34-windowed-again.json`、`logs-run1/` |
| 最近记录与重启恢复 | 两条媒体保存到历史；无参数重启为空闲且不自动播放，最近列表恢复并能打开 MP4 | `history-before-restart.json`、`37-restarted-idle.json`、`39-recent-restored-confirmed.json`、`42-history-playback-visible.json`、`logs-run2/` |
| 视频区域空格 | 获得视频区域焦点后暂停，再恢复；恢复瞬间的 UIA 旧状态不单独作为成功依据，后续状态和 `pause=false` 日志确认 | `43-keyboard-pause.json`、`44-keyboard-resume.json`、`45-resume-confirmed.json`、`logs-run2/` |

run1 PID 27968、run2 PID 37684 均通过 Alt+F4 正常关闭，`run1-exit.json`、`run2-exit.json` 均为退出 0。`release-checks.json` 和原始会话日志哈希一致，均依次记录渲染资源按序释放、会话销毁、主窗口关闭且播放资源已释放。用户原本无 `recent-history.json`，`cleanup.json` 确认恢复为不存在，剩余 MpvShell 进程为 0；最终测试历史备份保留在包外。

控件自动隐藏时使用过期 UI 索引的操作失败未计为成功，重新观察后的有效状态及日志才用于判定。倍速控件焦点下空格会打开倍速菜单，因此视频区域空格暂停/恢复另行验证。GUI 中的等待、暂停、重播和倍速不属于稳态性能区间，也不扩大为长时间稳定性结论。

### 7.4. 封包与逐项核验

正常退出并将本机运行日志移至包外后，使用既有 `seal-candidate-package.ps1` 完成封包；未再修改已封包目录或源码 ZIP。原始输出为 `final-seal-candidate.log`，外部报告的准确文件名为 `MpvShell-phase3-candidate-20261029-193719-e7d814.verification.json`。

| 项目 | 实际结果 |
|---|---|
| 主 ZIP | `artifacts/candidates/MpvShell-phase3-candidate-20261029-193719-e7d814.zip`，134,879,664 字节，692 个文件条目 |
| 主 ZIP SHA-256 | `61CF2DF93E76D70A95A5FA1EE15090BBAC961B29F5CFA4173F07C260E1BA121C` |
| 源码 ZIP | 289 文件，SHA-256 `F2758F6955145193D6D676F850726FB9C1F714B863AB5445529D38D28E095193`，与构建时冻结身份相同 |
| 内容闭包 | 35 份原生许可来源、26 个 NuGet 包、15 份合成素材、4 个原生 DLL，来源/构建/通知与快照匹配，原生实际加载与 API 通过 |
| 独立复核 | 重新计算主 ZIP 与源码 ZIP 哈希一致；逐个读取 692 个 ZIP 文件并与最终目录 SHA-256 对比，692/692 匹配，无缺失或额外文件 |
| 本轮摘要 | 包内 `Acceptance-20261029.json` 仅保存验收摘要与外部 7 份证据哈希，7/7 重算匹配；完整运行日志和用户视频留在包外 |

`package-verification.json` 与 ZIP 旁 `.verification.json` 分别记录内容闭包和封包身份，`.sha256` 保存 ZIP 校验和。ZIP 文件名未出现用户视频或运行日志，包内媒体按 15 份合成素材清单核验。摘要引用的 `gui-final/summary.json` 保持封包时原始哈希；§7.3 对恢复时 UIA 异步旧状态的说明及 `45-resume-confirmed.json` 属后补说明，不修改已固化证据。

## 8. 当前结论与未验证范围

P3-00 在当前声明的 RTX 3070 范围内通过：最终采用的两配置默认/显式回归、两份真实媒体完整 EOF 复验、修复后最终候选 GUI、两次正常退出、最近记录重启恢复及封包逐项核验均完成。修复前源码上的初始四轮、11 项短生命周期与 S4/S5 结果分别登记；两份首次 EOF 失败、修复后 All 轮环境清理失败和 10/28 原生故障历史均保留。P3-01 取得本机对照数据，不等于历史 PERF-02 归因关闭；P3-02 仅完成有限短时场景。

940M/Intel 历史崩溃及关联挂起跟进已完成（2026-10-29 按用户要求关闭），不再列为待办。长时间运行及资源持续增长、真实触屏、跨屏与其他 DPI/GPU、真实驱动重置和 HDR 仪器测量继续保留。该产物是当前功能基线候选，P3-00 通过不表示 P3-01/P3-02/P3-03 或整个 Phase 3 已完成。
