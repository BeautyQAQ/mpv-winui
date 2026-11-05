# P3-00 合并版本候选与最终目录验收

> 日期：2026-11-05
> 状态：通过（RTX 3070 本轮候选范围）；Phase 3 尚未完成最终阶段验收。
> 原始证据：`artifacts/p3-00-release-20261105/`
> 发布入口：[候选及历史包](../release-status.md)

## 1. 来源与执行范围

用户要求推进下一步：提交合并后的连续测试能力和历史记录，补显式素材回归与有限连续短测，生成含 11 月 4 日兼容修复的新候选，并验证最终目录和封包。源码已固定在干净的 `28ffe46aa87aba4a8fedfcc7bdf9f606c41fc5c0`，`sourceDirty=false`。本轮未新增产品功能或改动测试断言；后补验收文档不在冻结的 303 文件源码 ZIP 内。

本机为 RTX 3070，驱动 `32.0.16.2101`，Windows `10.0.26300`，SDK `10.0.403`，候选运行时 `.NET 10.0.14`。复用 10 月 29 日固定合成素材，18 份媒体与 5 份清单在本轮前后逐项哈希一致。194 项构建输入在复验前后及候选快照之间全部一致，见 `candidate-input-comparison.json`。没有将本机结果扩展为 HD 4600 新候选实机复验；其既有设备验收和接受范围继续保留。

## 2. 首轮配置失败与正确范围复验

首轮误启用仅用于旧 Intel 自动回退的 `MPVSHELL_TEST_TS_SOFTWARE_MEDIA`；RTX 3070 实际按产品策略使用 `d3d11va`，该测试要求 `no`，因此失败。另设置的 `MPVSHELL_TEST_HEVC_SOFTWARE_MEDIA` 并非测试使用的变量，HEVC 旧设备回退测试仍跳过。首轮 Debug **563/1/1**、Release **547/1/1**（通过/失败/跳过），原始报告位于 `explicit-baseline/`，不改写为通过。

复验前登记 `scope-correction.json`，移除误用变量，并明确两项旧 Intel 专用测试在 RTX 3070 上不适用：

- `Software_fallback_should_seek_without_reference_errors_on_legacy_Intel`
- `Hevc_main10_should_render_4k_frames_using_explicit_software_fallback`

使用 `media-inputs-rtx3070.json` 重新运行，保留硬解、TS 硬/软对照、HEVC/AV1、PQ 像素、HLS/字幕/多轨和自然播放场景。两配置构建均 0 警告、0 错误。

| 本轮结果 | 通过 | 失败 | 不适用跳过 | TRX / 命令退出 |
|---|---:|---:|---:|---|
| Debug 显式复验 | 563 | 0 | 2 | 4 / 0 |
| Release 显式复验 | 547 | 0 | 2 | 4 / 0 |
| 实际候选 DLL 的 Release 显式验证 | 547 | 0 | 2 | 4 / 0 |

通用 runner 的严格规则仍要求显式轮零跳过，因此复验的 `allCommandsPassed=false`、runner 退出 1 保持原样。`scoped-regression-assessment.json` 单独核对命令退出、4 份 TRX、零失败、194 项输入以及上述两个精确跳过名称后，登记本机范围通过；未放宽产品断言。功能或自然播放测试完成不新增吞吐性能通过结论，也不重开已获接受的 PERF-02。

候选 DLL 与普通 Release 构建分别记录。实际交付验证将 35 份产品 DLL/PDB/原生布局文件临时复制到四个测试输出，以解决方案入口 `--no-build --no-restore` 运行；测试前后候选哈希一致，随后 35 份文件全部恢复原哈希，见 `candidate-binary-explicit/`。测试主机验证与自包含 GUI 运行分别登记。

## 3. 同进程短测及恢复探针

执行前固定 2 份生成运动素材、2 轮、每次推进 1.5 秒、duration 0、进程上限 180 秒。实际同一进程完成 4 套媒体、24 个动作，用时 23.46 秒；打开、播放推进、暂停保持、恢复、停止归零及重播均通过，报告 Passed、退出 0、正常释放。

同次工具调用另跑既有 `playing` 与 `close-during-startup-load`，各自独立进程，分别约 3.43 / 1.03 秒，均通过并退出 0。两组 runner 均确认历史恢复；证据在 `continuous-generated/` 和 `legacy-recovery/`。两小时长测及资源趋势按用户要求暂缓，未执行。

## 4. 最终目录 GUI 与收尾

Computer Use 直接操作 `MpvShell-phase3-candidate-20261105-193728-2dc25a/MpvShell.App.exe`，观察与截图标识见 `gui-observations.json`，评定见 `candidate-gui-assessment.json`。

- 第一进程 PID 19276：TS 实际彩色画面；EOF 重播；播放中暂停并保持约 18.738 秒，暂停跳转到 3.738 秒；恢复；1.5×；停止归零和重播；F11 全屏/Esc 返回；切换包内 `multitrack.mkv`、正确 EOF；最近记录顺序为 MKV、TS。Alt+F4 退出 0。
- 第二进程 PID 2320：无参数启动，空播放界面、进度 0、没有自动加载；最近列表恢复原顺序；从记录打开 TS，实际画面推进。播放中 Alt+F4 正常关闭，日志最后位置约 21.971/30 秒，退出 0。
- 两次日志均确认图形资源、mpv 会话、主窗口按序释放。GUI 绑定的两份应用 DLL 均为 `54A4ADC4FC114A87E97D11221BC0FC4162B69160462C84DACA9DFD4663464358`。
- 开工用户历史不存在，结束后恢复为不存在；剩余播放器进程 0。GUI 历史快照与恢复证据保存在包外。

首次点击时 TS 已到 EOF，按实际重播登记；后续独立执行暂停和恢复，不将首次点击误记为暂停通过。UIA 即时文本有滞后，使用后续状态、截图和日志核对；不据短时画面宣称持续性能。

## 5. 封包结果

| 项目 | 结果 |
|---|---|
| 候选 | `MpvShell-phase3-candidate-20261105-193728-2dc25a` |
| ZIP | 134,883,056 字节，692 个文件逐项核验 |
| ZIP SHA-256 | `25452445ABAEACA940EB82342C29265AE4015A9B1E854E5A87844388BE3F17FE` |
| 源码 ZIP | 303 文件，SHA-256 `B07664C3C34A60A2696CB114B1898CC874421196B6DCD599BD33C6B930566FEB` |
| 来源 | 干净 `28ffe46`，`sourceDirty=false` |
| 核验 | 4 原生 DLL / AMD64 / Client API 2.5、临时与最终目录各 3 次会话烟雾、35 份原生许可来源、26 个 NuGet 包、15 份合成素材通过 |
| 包内摘要 | `Acceptance-20261105.json` 关联外部证据哈希；本机日志与用户历史文件在包外 |

P3-00 本轮候选更新完成。P3-02 有限短测通过、长测/资源趋势仍暂缓；P3-03 最终阶段验收未完成，触屏、跨屏、额外 DPI/硬件及物理 HDR 测量范围不扩展。已封旧 ZIP 和原始失败记录保留。
