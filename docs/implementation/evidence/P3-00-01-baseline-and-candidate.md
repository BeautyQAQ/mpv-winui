# P3-00 当前版本基线与候选测试包

> 状态：待验证；默认回归通过，显式两配置原生崩溃，最终目录 GUI 未验证。候选已封包并完成内容核验，验收限制随包交付。
> 日期：2026-10-28
> 对应计划：[P3-00 验收条件](../phase-3-plan.md#4-p3-00-本轮验收)
> 当前进度：[Phase 3](../phase-3-progress.md)

## 1. 范围与源码身份

本轮按用户要求建立当前版本基线，生成包含 Phase 2 功能的独立 Release `win-x64` 候选测试包。起始为干净的 `main / b9e4d2b560f8a96f6b206bfb6fbe418ced8d29af`。本轮修改候选发布、封包、基线工具及文档/索引，未修改产品源码；包内 `sourceDirty=true` 与 288 文件源码快照明确记录构建时输入，不能仅以起始提交替代。结果文档在构建后补齐，包内快照中的进行中记录保留当时状态，不据此修改二进制身份。

P2-05 历史记录为两配置默认各 525 通过/9 跳过、显式各 534 通过/0 失败/0 跳过，另有完整 GUI 与生命周期证据。本次检出中未找到 P2-05 原始汇总文件，也没有登记的 Phase 1 发布 ZIP。这些是历史产物在本机的可用性限制，不改写前序验收；本轮重新生成素材并保存独立证据，不称作旧产物哈希复核。

原始证据位于 `artifacts/p3-00/`。用户侧日期为 2026-10-28，机器清单保留实际 UTC/偏移时间。当前机器不同于历史 GTX 1060/RTX 3070 机器，不能直接继承其 GPU、驱动或性能结论。

## 2. 环境、输入与执行入口

| 项目 | 本轮实际值 |
|---|---|
| 起始提交 | `b9e4d2b560f8a96f6b206bfb6fbe418ced8d29af`，开工干净 |
| 构建输入 | 默认与显式各保存 188 项输入清单，轮内前后均一致；`candidate-build-input-comparison.json` 另确认基线、候选快照及当前工作区一致，产品源/测试无差异 |
| Windows、SDK、PowerShell | Windows 10.0.26300；SDK 10.0.403，运行时 10.0.14，PowerShell 7.6.5；见 `default-baseline/environment.json` |
| GPU、驱动与桌面 | 系统列出 NVIDIA GeForce 940M / 23.21.13.8873、Intel HD Graphics 4600 / 20.19.15.4624 以及虚拟显示适配器；实际渲染设备以场景报告为准 |
| 工具来源 | FFmpeg 8.1.2 essentials；下载归档 SHA-256 与站方及仓库已有记录一致，工具路径/哈希及下载过程见 `tools/tool-provenance.json` |
| 固定素材 | `media-hdr/` 三份 4K HEVC/AV1/PQ；`media-p1-04/` 视频 HLS、多轨、SRT/ASS、音频及视频；`media-ts/` 合成 TS；`media-perf/` SDR HEVC 4K60 30 秒 |
| 素材身份 | `media-inputs.json` 登记 17 份输入、5 份清单及测试变量；显式前后 22 处哈希全部匹配 |
| 执行入口 | `build/testing/test-candidate-baseline.ps1`；分别使用 `Default`、`Explicit` 和独立目录 `default-baseline/`、`explicit-baseline/` |
| 发布入口 | `publish-candidate.ps1`，标签 `phase3-candidate`、日期标签 `20261028`，输入为本轮 `media-hdr/` 和 `media-p1-04/` |

本轮素材由仓库生成器及 `prepare-media.ps1` 的 SDR 性能输入生成步骤产生，不含用户媒体。生成参数、工具哈希及 FFprobe 核对保留在素材目录，性能输入未设置 FPS 通过阈值，也未降低硬解要求。重新编码后的文件哈希属于本轮输入，不混用历史编码文件身份。

基线工具依次执行 `dotnet build mpv-winui.slnx -p:Platform=x64 -c <配置> -m:1 -v:minimal`，随后以 `--no-build --no-restore -m:1 --logger trx` 运行同一配置测试。默认运行清理所有 `MPVSHELL_TEST_*`，显式运行加载已登记变量；每轮结束恢复调用方环境。实际参数、时间、退出码和日志路径保存在各目录 `commands.json`。

默认轮执行后，独立审查修正了显式 P1-04 报告变量名称，并补素材前后哈希、runner 副本、异常失败记录和轮次完成检查；默认轮的原 runner 已另存，不把两轮脚本版本混为同一份。显式报告分别进入 `<配置>-explicit-hardware/`、`<配置>-explicit-p1-04/`，性能报告沿用硬件目录。

错误素材哈希负例用真实 `pwsh -File` 执行：在构建前退出 1，`failure.json` 记录哈希变化，收尾 `allCommandsPassed=false`、完成命令 0/4，无构建或产品测试。证据为 `runner-validation/direct-validation-result.json`。先前测试驱动自身的作用域失败单独保留，不作为产品失败或本次直接负例结果。

## 3. 构建与回归

| 配置 / 项目 | 实际结果 | 原始证据 |
|---|---|---|
| Debug 默认轮构建 | 0 警告、0 错误 | `default-baseline/Debug-build.log` |
| Debug 默认全量 | 525 通过、0 失败、9 跳过，退出 0 | `default-baseline/Debug-default.log` 与同名 TRX 目录 |
| Release 默认轮构建 | 0 警告、0 错误 | `default-baseline/Release-build.log` |
| Release 默认全量 | 525 通过、0 失败、9 跳过，退出 0 | `default-baseline/Release-default.log` 与同名 TRX 目录 |
| Debug 显式轮构建 | 0 警告、0 错误 | `explicit-baseline/Debug-build.log` |
| Debug 显式全量 | 已记录 529 项通过，随后测试主机原生崩溃；整轮失败、退出 1 | `explicit-baseline/Debug-explicit.log` 与同名 TRX 目录 |
| Release 显式轮构建 | 0 警告、0 错误 | `explicit-baseline/Release-build.log` |
| Release 显式全量 | 已记录 529 项通过，随后测试主机原生崩溃；整轮失败、退出 1 | `explicit-baseline/Release-explicit.log` 与同名 TRX 目录 |

默认两轮的 525 项由 App 297、Abstractions 2、LibMpv 156、Rendering 70 组成，Rendering 另有 9 项条件测试跳过。显式两轮各已记录 App 297、Abstractions 2、LibMpv 156、Rendering 74 通过；部分条件测试有完成结果，但整轮未完成，不能写成“529 项全量通过”或以局部 TRX 的零失败宣称成功。

两配置控制台均记录测试主机 `Fatal error. 0xC0000005`，调用栈为 `MpvNative.RenderContextRender` → `MpvRenderContext.Render` → `PlaybackPerformanceTests.PerformanceProbe.Render` → `RenderWorker.Run`。`dotnet test` 均退出 1；`explicit-baseline/closure-verification.json` 为 `allCommandsPassed=false`，虽 4 个构建/测试命令都已执行，仍保留失败结论。原生崩溃的具体原因尚未确认，不能仅依据当前显卡型号归因，也不能写成一般断言失败。

默认与显式测试分别保存原始日志、逐项 TRX 汇总及运行身份。显式结束时素材及清单哈希仍一致；没有为取得通过结果放宽测试条件。与默认轮发现的 534 项逐项比较，两配置均缺少 HEVC、AV1、PQ、性能测量自检、自然播放性能这 5 项完成结果，见 `native-crash/missing-results.json`。缺少结果不能记为通过或跳过，也不能从未返回结论推断各项独立运行必然失败。

### 3.1. 事件日志与单项诊断

`native-crash/events.json` 通过 Rendering 测试主机路径和 ReportId 关联两轮 Application Error / WER：故障模块均为 `coreclr.dll` 10.0.1426.52326，异常 `c0000005`，偏移 `0x3590bf`。事件日志的故障模块和偏移不是根因结论，不能据此认定 CLR、驱动或其他组件负责。

随后保持原 Debug 二进制、相同素材和性能参数，以解决方案入口只筛选自然播放性能用例，附加 `--blame --blame-hang-timeout 2m --blame-hang-dump-type none`。`performance-isolated-debug/result.json` 记录命令退出 1、`binariesUnchanged=true`、输入与显式轮相同；`test.log` 显示两分钟无活动后由超时机制终止主机，未返回测试完成结果。保留了 blame 序列文件，没有取得 dump，也没有复现两轮显式回归的同一 `0xC0000005` 栈。

该诊断确认相同场景单独执行仍未完成，但不能据此宣称原生崩溃与测试序列无关、已经定位或已经修复。后续优先诊断原生崩溃与性能用例挂起；PERF-02 的吞吐对照及通过判定尚未开始，不能将故障定位尝试混写为性能验收。

## 4. 候选目录与封包身份

| 项目 | 本轮实际值 |
|---|---|
| 候选名 | `MpvShell-phase3-candidate-20261028-121903-72991b` |
| 最终目录 | `artifacts/candidates/MpvShell-phase3-candidate-20261028-121903-72991b/` |
| 身份 | `artifactKind=candidate`；`b9e4d2b...` 加本轮工具/文档工作区，`sourceDirty=true`；288 文件源码快照 |
| 配置 | Release / `win-x64`，.NET 与 WinUI 自包含；SDK 10.0.403 |
| 原生闭包与烟雾 | 四 DLL 与锁定清单一致、AMD64、实际加载成功；Client API 2.5；临时和最终目录各 3 次会话创建/初始化/销毁 |
| 许可及素材 | 35 份原生许可来源文件、26 个托管包许可/通知/元数据、15 份包内功能素材，封包时逐项核验通过 |
| 源码 ZIP | `MpvShell-phase3-candidate-20261028-121903-72991b-source.zip`，288 文件核验通过；SHA-256 `834235CE62C1D6AB19664EB8242EB4FA9A5141F27D3BF8C28C08651B720FD66A` |
| 主 ZIP | `MpvShell-phase3-candidate-20261028-121903-72991b.zip`，134,886,266 字节，694 条目逐项匹配 |
| 主 ZIP SHA-256 | `1767214D187DB69C42E95076793C0B99965029BA31EC0E11A9778BA17DAC6E89` |
| 发布与身份记录 | 包内 `build-info.json`、`publish-verification.json`、`source-hashes.json`、`package-verification.json`、`package-content-sha256.json`；包旁 `.verification.json` 与 `.sha256` |
| 验收限制随包交付 | `baseline-summary.json`、`baseline-evidence-sha256.json`、`候选验证清单.md` 已进入主 ZIP，明确候选未通过完整验收 |

候选源快照包含构建时尚在进行中的结果文档，本记录在构建后补齐，产品源码没有修改。候选和历史 Phase 1 的名称、说明及日志前缀已分开；原 `publish-phase1.ps1`、`seal-phase1-package.ps1` 保留兼容入口。候选包内的功能来自当前源码，但完整验收尚未满足，不能替代历史已通过包的状态。

## 5. 最终目录运行与清理

2026-10-28 从最终候选路径启动测试进程 7340，进程创建了标题为 MpvShell 的窗口。自动化工具首次及重新绑定均失败，`cua_repl` 原生窗口接口也不能取得目标 UI 状态；错误涉及窗口归属验证与 `SwitchDesktop.App.exe`。没有取得可判定的实际画面或交互结果，不把进程/窗口创建写成播放验收。

| 场景 | 本轮实际结果 / 证据 |
|---|---|
| 启动 | 最终目录进程与窗口已创建；`gui/candidate-identity.json`、`gui/gui-blocker.json` |
| 实际画面、打开媒体与常用控制 | 未验证；未能执行 GUI 操作 |
| 停止/重播、换媒体 | 未验证 |
| 正常退出、无参重启与最近记录恢复 | 未验证；仅清理本次启动且路径匹配的空闲测试进程，属于强制结束 |
| 用户历史与日志 | `gui/cleanup.json` 确认测试进程已消失、历史恢复（开始时历史不存在）、日志移到包外 |
| 日志收集 | `Collect-Logs` 成功收集 1 次运行，输出 `MpvShell-phase3-candidate-Logs-20261028-122552-18c04814.zip`；见 `gui/collect-logs.log` |

GUI 工具失败与显式回归的原生崩溃是两项独立问题。GUI 工具失败不能证明播放器画面有缺陷，也不能据窗口创建或日志收集成功认定最终目录播放正常。强制结束不计正常退出，不具备按序释放、重启恢复或实际画面的本轮通过证据。

## 6. 当前结论与剩余工作

P3-00 标记为**待验证**。当前已完成默认回归基线、素材身份、候选生成与封包内容核验；显式两轮原生崩溃，以及最终目录 GUI/正常退出未验证，使完整基线与候选验收尚未满足。主 ZIP 已作为明确标注限制的候选测试产物登记，不能改记为已通过发布。

下一步优先诊断原生渲染崩溃与单项性能挂起，在可操作桌面补最终目录画面、打开/控制、停止重播、历史保存/重启与正常退出证据，再决定能否关闭 P3-00。当前候选与旧 Phase 1 通过包分别在 [唯一入口](../release-status.md) 记录，不覆盖旧身份，也不预先记载提交、推送或外部分发。

PERF-02、长期播放、真实触屏/系统触控取消、跨屏/其他 DPI/GPU、实际驱动重置和 HDR 仪器测量保持原有独立状态。固定素材回归或候选生成不代表 Phase 3 全部通过；未覆盖媒体格式同样不由单一样本结果泛化。
