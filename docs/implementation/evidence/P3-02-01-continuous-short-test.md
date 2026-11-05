# P3-02 同进程连续短测与正常收尾

> 状态：本轮两项同进程连续短测、正常收尾及补充回归通过；两小时长测及资源趋势按用户要求暂缓，本轮未执行。P3-02 仍进行中。
> 日期：2026-10-29
> 对应计划：[Phase 3](../phase-3-plan.md)
> 原始证据：`artifacts/p3-02-short-20261029/`

## 1. 授权范围与身份

用户批准补同进程连续测试能力和正常退出/释放验证，明确暂缓两小时长测及资源趋势。此次新增 Debug 验收探针与 runner 支持，只用于有限功能和稳定性验证。已封 `MpvShell-phase3-candidate-20261029-193719-e7d814` 及其源码 ZIP 保持冻结，来源和 P3-00 结论不因本次源码前进而更新。940M/Intel 历史崩溃及关联挂起跟进保持已完成，不重新开启。

本轮基线仍为 `4df49f5214116d655a3ac096ce566b4a707d89f5` 加工作区中的连续探针、runner 和解析测试修改，固定记录为 `source-for-validation.json` 与 `run-identity.json`。实际使用 .NET SDK 10.0.403、PowerShell 7.6.5、Windows 10.0.26300，Debug / `win-x64`；两项应用报告均确认 RTX 3070、驱动 `32.0.16.1742`、D3D11 调试层启用及 1658×1084 表面。两份用户视频只作本机输入，不修改或加入分发包。

`source-for-validation.json` 固定 165 项源码/工具身份，重算无差异；`run-identity.json` 的 4 份 Debug 应用/渲染/播放器二进制重算匹配，两次 invocation 的 exe/DLL 也与之相同。应用 DLL SHA-256 为 `4C346128B3AAAA107886C2AD19F78B005516B8A34EEF2E5C9A57F5B5B700CD76`，runner 为 `EE8AA7BCFCE988167FE4FB68A9405E36720A4B72375A53F80818A1A34254D1D4`；本轮身份不替代已封 Release 候选的身份。

## 2. 执行前计划与判定条件

执行前计划保存在 `test-plan.json`，显式选择 `-Mode continuous`，不加入默认 11 项独立进程场景。

| 场景 | 媒体数 | 轮数 | 每次播放推进 | duration | 进程上限 | 状态 |
|---|---:|---:|---:|---:|---:|---|
| 两份生成运动素材 | 2 | 2 | 1.5 秒 | 0，按轮数结束 | 180 秒 | 通过 |
| 用户 TS 与 MP4 | 2 | 1 | 1.5 秒 | 0，按轮数结束 | 180 秒 | 通过 |

每轮对所有计划媒体依次验证打开、播放推进、暂停保持、恢复、停止归零及重播。同次运行要求 PID、会话和渲染器实例保持一致；每步保留实际状态和完成计数，不能用先前累计帧数代替本步播放证据。重复路径、缺失媒体须在运行前拒绝，原始负例保留。

通过须同时满足 `Status=Passed`、`ContinuousCompleted=true`、完成轮数等于计划、无页面错误，及 `ShutdownCompleted`、`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe` 均为 true。runner 还须核对启动 PID、退出码 0，以及渲染资源、会话、窗口的释放顺序。缺报告、超时、非零退出或不完整计数均不能记为通过；失败目录与复验结果分别保留。

连续场景由 Debug 探针调用应用关闭/释放入口后关闭窗口，其证据与用户 Alt+F4 的窗口操作分别说明。原用户最近历史的存在性与原字节、调用环境须在结束后恢复；超时只清理本次启动的进程。

## 3. 实际执行与退出证据

### 3.1. 构建修正、默认回归与输入负例

首次 Debug 解决方案构建退出 1：新增 `AppRecoveryContinuousPlanTests` 在 FluentAssertions 的表达式树中使用 `is/or` 模式，触发 `CS8122`。随后仅改为等价的异常类型比较，重新构建退出 0、0 警告/错误。首轮 `Debug-build.log` 与 `Debug-build-result.json` 保留，复验另存为 `Debug-build-retest.log` 和 `Debug-build-retest-result.json`，不写成首次构建通过。

同一固定 Debug 构建的默认全量命令退出 0，4 份原始 TRX 分别为 App 313、Player 2、LibMpv 156、Rendering 70 通过及 9 跳过，合计 **541 通过、0 失败、9 跳过**；其中 16 项为新连续计划解析测试。原始目录为 `Debug-default/`，日志和命令退出记录分别为 `Debug-default.log`、`Debug-default-result.json`。默认跳过仍需要显式媒体/性能输入，不能说本轮重新执行了上一轮全部显式场景。

runner 实际拒绝 9 类输入：相对路径、单路径、重复一对、两份不同路径中再含重复项、与首素材冲突的启动路径、未选模式却传连续参数、零轮数、NaN 播放秒数及缺失文件。前 8 项见 `runner-validation-46c5f4aed4544a0dacf968c809fcffb7/negative-inputs.json`，缺失文件见 `negative-missing-media.json`，均未启动应用且历史未改。`helper-validation.json` 另确认脚本语法、exe/DLL 身份记录，以及环境变量 absent/empty/value 三种状态恢复。

此前 `runner-validation-bba827b42e6546ce9149107cd6ca26d2/negative-inputs.json` 的 8 项全部停在验证驱动的错误参数展开/绑定阶段，未触达预期拒绝条件；原始记录保留，不计为产品负例通过或产品失败。

### 3.2. 连续短测与收尾

两次 invocation 均明确记录 `LimitSeconds=180`，未用自动预算扩大显式上限；实际计划与 `test-plan.json` 一致。两项 runner 命令均退出 0，应用报告 `Status=Passed`、`ContinuousCompleted=true`、完成轮数与计划一致；逐项 `Completed=true`，所有 Action 均 Passed，无注入故障或页面错误。

| 运行 | PID / 会话实例 | 完整媒体套数 / 动作 | 连续场景耗时 / runner 总耗时 | 关闭耗时 | 结果 |
|---|---|---|---|---|---|
| 生成素材 | 39416 / 41249094 | 4 / 24 | 21.529 / 23.268 秒 | 0.037 秒 | 2 轮完成，报告 Passed、进程 0 |
| 用户视频 | 38508 / 10785056 | 2 / 12 | 11.294 / 16.982 秒 | 0.068 秒 | 1 轮完成，报告 Passed、进程 0 |

每套含打开、播放推进与非黑画面、暂停稳定、恢复、停止清零、停止后重播 6 个动作。各运行只使用一个 PID/会话/渲染器/VM 实例，按计划顺序覆盖全部媒体；报告中的逐媒体原生路径与计划一致。生成素材 12 次推进检查最小推进 1.5 秒、46 帧，用户视频 6 次最小推进 1.5015 秒、37 帧，18 次均读到非黑画面。6 次暂停的原生位置差与像素平均差均为 0；6 次停止均为 `IsStopped=true`、位置/时长 0、原生路径 null。这里只断言非黑及推进，不新增每步运动像素差或吞吐结论。

两项均确认 `ShutdownCompleted`、`SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe=true`。原始会话日志含正确 PID，且最终渲染资源按序释放、会话销毁、窗口关闭标记按序出现；不是仅检查标记存在。生成素材日志 SHA-256 为 `DEFBFC320528C26B601414706672EAD75FA55A396CDD75EA0BC2B5A2530CDA7A`，用户视频日志为 `DE1151E9D740E79A1B75E6DD10B6112E1092AA0838EFA4189C56009ECD41AEED`。原始报告、计划、invocation、summary、history-recovery 与日志分别保存在 `generated/`、`user-videos/`，命令退出见 `generated-result.json`、`user-videos-result.json`。

生成素材运行前用户历史不存在，runner 恢复为不存在。用户视频运行前特意写入合法空历史夹具，以实际验证“原文件存在”的分支：runner 按原字节恢复 34 字节夹具，前后 SHA-256 均为 `DA6FA0FB6DC64A3FAF1F81F830B8696CCF02B9C654135111429090FA240870F8`；执行端随后恢复用户真正原状态（不存在）。两级恢复分别记于 `user-videos/history-recovery-*.json` 和 `user-videos-result.json`，后者确认剩余应用进程 0。独立检查同样为历史不存在、进程 0。

### 3.3. 补充回归

Release 解决方案构建退出 0、0 警告/错误，默认全量的 4 份原始 TRX 为 **525 通过、0 失败、9 跳过**，命令退出 0。新增解析测试及探针限定 Debug，Release 因而比 Debug 少 16 项；两配置默认结果见 `regression-final-summary.json`，按逐项 `UnitTestResult` 汇总跳过，未把适配器的 `Counters.notExecuted=0` 误作无跳过。

runner 新增全局收尾后，另在同一次调用中串行回归 `playing` 与 `close-during-startup-load`，每项仍为独立应用进程，不能计入连续运行的同一 PID。执行前以 `legacy-smoke-plan.json` 固定两模式/90 秒上限，并预种两个“存在且为空串”的诊断环境变量。

| 旧模式回归 | PID | runner 耗时 | 关键结果 |
|---|---:|---:|---|
| `playing` | 33420 | 3.470 秒 | 两次注入故障、恢复后继续播放及终态断言通过，正常收尾 |
| `close-during-startup-load` | 28256 | 1.021 秒 | 真实启动 HTTP 仍待加载时关闭，关闭 0.099 秒、加载完成 0.100 秒，同一 5 秒预算内结束 |

两项原始报告均 Passed、退出 0、4 项释放布尔为 true；两份日志的最终释放顺序正确。`legacy-smoke-result.json` 确认两环境变量恢复为原来的存在空串，历史恢复为不存在；该轮验证了多模式同次 runner 调用的收尾。

最终 `completion-integrity.json` 确认 165 项源码/工具、4 份 Debug 二进制及两份用户视频未变，原历史不存在已恢复，剩余应用进程 0。此记录同时明确 `longRunExecuted=false`、`resourceTrendExecuted=false`；源码或工具验证后没有据此重封旧包。

## 4. 当前边界

本轮授权的同进程连续短测能力及正常收尾/释放验证已完成，并通过两配置构建、默认回归及旧模式回归。这里只覆盖预定有限循环，不做吞吐判定，不证明长时间稳定性、资源持续增长、实际驱动重置或额外硬件兼容。两小时长测和资源趋势是用户暂缓项，状态为未执行；后续获得执行安排后另行登记条件与结果。P3-02 与整个 Phase 3 保持进行中。
