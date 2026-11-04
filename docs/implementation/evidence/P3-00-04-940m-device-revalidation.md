# P3-00 940M / Intel HD 4600 原设备复验

日期：2026-11-04。用户确认当前设备就是 10 月 28 日的 940M / Intel HD 4600 机器，并授权使用 Computer Use 验证。

本记录保存修复前的首轮失败证据。同日后续代码修复和复验见 [Intel HEVC 兼容修复](P3-00-05-940m-hevc-compatibility-fix.md)，不覆盖下述原始结果。

后续状态：本设备已完成修复、回归及 GUI/H.264 验收，用户要求全部收口，见 [完成登记](P3-00-06-940m-gui-and-h264-acceptance.md#本设备完成登记)。下述失败和待验证描述仅记录修复前当时状态，不作为当前设备待办。

**原故障已复现，不能清理为过期待办。** 原 Debug 测试二进制、原素材及参数的独立自然播放用例再次发生 `0xC0000005`，调用栈仍经 `MpvNative.RenderContextRender`、`PerformanceProbe.Render` 和 `RenderWorker.Run`。Computer Use 两个接口均受窗口归属错误阻挡，未取得本轮 GUI 操作证据。P3-00 保持待验证。

## 1. 身份与执行边界

原始证据根目录为 `artifacts/p3-00/revalidation-20261104-940m/`，下文路径均相对此目录。日志、TRX、转储、媒体和辅助诊断脚本不随 Git 提交。

| 项目 | 本轮实际值 |
|---|---|
| 开始源码 | 干净 `main / d5a8bba`；从远端快进更新两个提交后开始 |
| 系统与工具 | Windows 10.0.26300 x64；SDK 10.0.403；共享 .NET 10.0.14；详细环境见 `environment.json` |
| GPU 列表 | NVIDIA GeForce 940M / `32.0.15.8278`，Intel HD Graphics 4600 / `20.19.15.5171`，另有 GameViewer 虚拟显示适配器 |
| 与旧环境差异 | 10 月 28 日 NVIDIA / Intel 驱动分别为 `23.21.13.8873` / `20.19.15.4624`；本轮为同机当前驱动验证，未回退或修改驱动、GPU 偏好及系统设置 |
| 原素材 | 沿用 `artifacts/p3-00/media-inputs.json`，17 份输入及 5 份清单 SHA-256 全部匹配；不重新生成编码文件 |
| 原二进制 | 独立复验前，Rendering Debug 输出的 144 份 DLL 与旧 `performance-isolated-debug/binaries-before.json` 全部匹配；复验后再次核对未变 |
| GUI 候选 | 本机保留的 `MpvShell-phase3-candidate-20261028-121903-72991b`，由原 `Start-Debug.ps1` 启动；11 月 3 日另一台机器的新候选未在本机重新生成或冒充原包 |
| 产品源码关系 | 当前 112 份 `src/` 文件与原候选 `source-hashes.json` 全部匹配，见 `candidate-product-input-comparison.json`；最新测试与工具的变化另由本轮基线身份记录 |
| 实际渲染设备 | 原候选启动日志和 Debug 默认自检均报告 Intel HD 4600 / ANGLE / D3D11，非 WARP、调试层未启用。不能凭系统列出 940M 宣称已走 NVIDIA 渲染；崩溃独立轮未产生完整设备报告 |

## 2. 原二进制独立复验

`run-legacy-isolated.ps1` 从解决方案入口执行 Debug `--no-build --no-restore`，仅筛选 `Natural_playback_throughput_should_be_measured_and_reported_separately_from_functional_checks`。沿用原 4K60 SDR HEVC、3840×2160、`d3d11va`、vsync 1、预热 3 秒、稳态 20 秒、无窗口内读回和原硬解要求，未放宽断言。

本轮附加 crash/hang 收集，超时 2 分钟。14:15:06～14:16:08（北京时间）命令退出 **1**；14:15:29 的 .NET 事件和测试输出都记录原生 `0xC0000005` 及上述栈。此次独立执行复现了崩溃，**没有以两分钟挂起超时结束**，不能写成同时复现原挂起或已经确定根因。

证据为 `legacy-isolated-debug/{result.json,test.log,media-validation.json,original-binary-validation.json}`、TRX、Blame 执行序列及 `application-events.json`。系统事件将故障模块登记为 `coreclr.dll / 10.0.14`，该模块名称不足以判断损坏起点或归因到运行时。

收集器尝试写入转储，但报告 `MiniDumpWriteDump` / `ReadProcessMemory or WriteProcessMemory` 失败（HRESULT `0x8007012B`）。附件中保留 1,445,722,221 字节的 `.dmp` 及测试框架复制件；按文件头和流目录检查，头部声明 19 个流，但目录条目全部为零，未取得可读取的异常、模块或线程流。**这不是可用于完整原生栈分析的有效转储。** `inspect-dump.py` 和相邻 `dump-structure.json` 保存检查过程；没有安装调试器或修改 WER 全局设置。

## 3. 最新源码回归

使用仓库 `test-candidate-baseline.ps1` 的本地诊断副本 `run-current-baseline.ps1`；仅调整副本的仓库相对路径并在已有 Blame 参数上增加 crash mini 收集。执行的副本、SHA-256、构建输入、二进制、媒体前后检查、每轮命令和逐项 TRX 保存在 `current-baseline/`。通过解决方案入口分别构建并运行 Debug/Release 默认及显式素材场景，产品和测试源码在执行期间冻结。

两配置构建均为 **0 警告、0 错误**。6 条构建/测试命令均已执行，189 项构建输入及 22 处媒体/清单哈希前后一致；`closure-verification.json` 的 `allCommandsPassed=false`。

| 本轮 | 完成结果 | 整轮结论 |
|---|---|---|
| Debug 默认 | 525 通过、0 失败、9 条件跳过；4 份 TRX | 命令退出 0，通过 |
| Debug 显式 | 529 项记录 Passed，测试主机随后在自然播放用例的原生渲染调用崩溃；4 份 TRX | 命令退出 1，失败，不能把局部通过计数写成全量通过 |
| Release 默认 | 525 通过、0 失败、9 条件跳过；4 份 TRX | 命令退出 0，通过 |
| Release 显式 | 同为 529 项记录 Passed，仍在自然播放用例经 `RenderContextRender → PerformanceProbe.Render` 发生 `0xC0000005` | 命令退出 1，失败 |

两显式轮均缺少 HEVC Main10、AV1、PQ 梯度、测量自检和自然播放五项的完成结果；不能将它们登记为五项断言失败或跳过。Debug 默认的小样本测量自检实际通过，原始报告另存为 `debug-default-harness-selfcheck.json`。两轮 crash 附件也都因 `0x8007012B` 写入失败：Debug / Release 文件分别为 5,766,987 / 55,742,310 字节，结构检查同样未得到有效异常或模块流。三次崩溃的日志和部分转储全部保留。

### 3.1 ProcDump 收集补测

为取得可分析的证据，从 [Microsoft Sysinternals 官方入口](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump) 下载独立 ProcDump，仅保存于本轮 `tools/`，校验 Microsoft Corporation 有效签名并登记来源、版本及 SHA-256。按 [VSTest 官方选项](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest) 以进程级 `VSTEST_DUMP_FORCEPROCDUMP=1` / `PROCDUMP_PATH` 强制收集，不注册 AeDebug 或安装全局事后调试器。

`run-procdump-isolated.ps1` 使用本轮最新 Debug 构建、不重建，沿用相同素材及性能参数，14:29:05～14:31:32 再次单独运行自然播放用例。本轮明确发生 **2 分钟无活动超时，命令退出 1，无完成测试结果**；没有得到上述 `0xC0000005` 的新栈，不能将通用“测试主机崩溃”提示另算一次同形原生崩溃。

取得 `current-isolated-procdump/729a97dd-4297-488d-92c7-e520b7785c86/testhost_4668_20261104T143127_hangdump.dmp`，41,831,754 字节。结构检查可读取 20 个流、145 个模块、38 个线程及其上下文；这是挂起采样，没有 ExceptionStream，不能当作已捕获原始崩溃异常。`dump-structure.json`、Blame 序列、日志和 `result.json` 均保存，执行前后 DLL 哈希未变。仍需分析线程栈及原生等待，并继续取得有效崩溃转储；附加调试器会改变运行条件，尚未证明本次超时与旧崩溃/挂起同根因。

### 3.2 小样本真实应用探针

完整基线结束后，从最新 Debug 应用运行既有 `test-app-recovery.ps1 -NoBuild -Mode playback`，输入为原 `multitrack.mkv`（320×180 H.264 / 24 fps / 12 秒），记录在 `app-playback/`。本轮沿用探针的原断言，没有修改产品或测试代码。

原始报告验证初始非黑 GPU 画面，实际 Intel HD 4600、D3D11 GPU 纹理解码，并在约 6.313 秒采到变化画面；日志明确记录 EOF。结束阶段 `ReadMpvCountersAsync` 读取原生属性返回 `property unavailable (-10)`，探针及驱动命令整体失败，不能登记完整播放探针通过，也不能将该诊断读取错误与 4K 原生崩溃混为同一问题。短样本仅取得一次间隔采样，亦不构成该探针所要求的两次变化样本证据。

失败收尾中资源按序释放，`ShutdownCompleted`、`SessionReleased`、`RendererReleased` 和重复关闭检查均为 true，约 0.116 秒完成关闭，应用进程退出 0；这是探针触发的真实应用关闭证据，**不是 Computer Use 目视或用户操作通过**。

## 4. Computer Use 验证受阻

按 `computer-use` 技能，先使用 `node_repl` 的 `@oai/sky`。直接 `launch_app` 未产生可选的播放器窗口；随后通过原包诊断启动器启动进程 21428，从 `list_windows()` 取得标题为 MpvShell 的唯一窗口 722380，但工具将其归属登记为 `SwitchDesktop.App.exe`。首次绑定、刷新清单后不传 app 的重新绑定均失败；另一个已提供的 `cua_repl` 接口从其清单选取同一窗口，仍返回同样的错误：

```text
window id 722380 no longer belongs to {6D809377-6AF0-444B-8957-A3773F02200E}\Switch Desktop\SwitchDesktop.App.exe; current owner is {6D809377-6AF0-444B-8957-A3773F02200E}\Switch Desktop\SwitchDesktop.App.exe
```

未取得截图、控件树或成功的 UI 输入，不登记画面、播放/暂停、停止重播、倍速、换片、历史恢复或正常退出通过。启动日志证明原候选建立了 Intel HD 4600 渲染上下文并提交首帧，不能代替目视画面或 GUI 验收。只强制清理了本轮创建且路径核对匹配的空闲测试进程；启动器记录退出 -1，**不计正常退出**。

错误、进程身份及完整启动日志保存在 `gui-blocker.json` 和 `gui-attempt-logs/`，运行日志已移出原候选目录。

## 5. 当前结论

旧设备已经可以访问，原二进制独立轮和最新两配置显式轮均复现原生崩溃；ProcDump 附加后的最新 Debug 独立轮发生两分钟无活动超时，现已取得可读取的挂起转储。下一步分析线程栈及原生等待，继续取得有效崩溃转储并定位 Render API 调用故障；恢复可绑定的 Computer Use 窗口后补做本设备实际操作。各轮执行条件和原始结果分别保留，尚未判定共同根因。

用户最近记录原先不存在；GUI 尝试后仍未创建，探针仅写入本轮测试素材。已将测试历史另存 `history-after-probe.json`，核对唯一条目为本轮素材后恢复原先不存在的状态，见 `history-restoration.json`。原候选清单的 693 项内容全部匹配，主 ZIP / 源码 ZIP 哈希仍分别与旧登记的 `1767214D...` / `834235CE...` 一致，日志移至包外；未覆盖包或更改候选源码快照。

GTX 1060 / UU 已获接受的 P3-01 / PERF-02 结论不因本设备复验而改写；P3-00 保持待验证，P3-02/P3-03 和整个 Phase 3 没有由本轮关闭。
