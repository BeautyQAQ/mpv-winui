# 缓冲状态与应用内渲染恢复验收（2026-09-11）

本次补齐 `BufferingChanged` 的 ViewModel / XAML 绑定，并使用真实 PlayerPage、SwapChainPanel、libmpv、ANGLE 和 D3D11 验证故障恢复。按要求重新审查了前次实现和证据，补充了原先未覆盖的真实缺陷；以下为重新验收结果，替代早先“140 项测试、8 次应用场景”的结论。保留工作区已有的 HDR / 硬解改动。

## 修复与复现

- 缓冲状态独立于播放/暂停状态；`IsBuffering`、`BufferingVisibility` 驱动中央提示。UI 调度后更新，换媒体、加载失败、EOF、后端故障及事件流异常清理旧状态；关闭后忽略排队回调。
- 后端容量 256 的 DropOldest 事件队列可能丢掉唯一的 `BufferingChanged(false)`，造成永久转圈。现在每个排队事件携带当时的缓冲状态，消费者在保留事件之前补发必要的状态转换，保持时序；媒体结束后忽略迟到的 cache=true。新增 15 个后端回归用例。
- 原恢复流程释放活动 `mpv_render_context` 会让 libmpv 关闭视频轨。真实应用基线复现了“重建成功日志 + 永久黑屏 + video output initialization failed (-15)”：`artifacts/app-recovery/baseline/`。
- 现在重建前在命令线程暂停并停用视频轨，重建后恢复原轨道、可定位位置与原暂停状态。使用恢复租约串行化播放修改，并校验原生 playlist entry 身份，避免把旧状态应用到新媒体；重建失败也归还租约。
- 恢复通知标记在渲染线程重新允许呈现之前交还，旧恢复任务的 finally 不再清除新故障的标记。因此重建后的首帧再次失败能进入错误提示。
- 尺寸变化中出错原本会同时排队恢复并向页面抛异常，导致恢复成功仍保留错误。真实窗口 resize 基线复现见 `artifacts/app-recovery/reaudit-resize-baseline/`；现在 resize / 输出切换将可恢复错误交给恢复流程，仅最终失败报告给页面。
- 严格的 LG 暂停验收复现了恢复位置由 30.3303 跳至 31.3313 秒：TS demux 的 exact seek 落点过晚。固定 `hr-seek-demuxer-offset=1` 预留解码区间；避免临时恢复该选项与异步 seek 处理竞争。修复后实际暂停位置 29.9966333333 秒在重建前后不变，采样像素差异为 0。失败证据：`artifacts/app-recovery/reaudit-lg-settled/`。
- 当前仍采用一次自动重建的策略。重复故障或重建失败进入不可恢复终态：暂停原生媒体、停止呈现、禁用播放入口、保留错误提示；音量成功不能清掉错误，显示器状态变化不能重新开始呈现，Esc/F11 仍可退出全屏。
- 原生终态锁在播放修改锁内建立，阻止已排队的播放、loadfile、seek、选轨命令重新启动媒体；仍允许读属性、音量、静音、幂等暂停和关闭。ViewModel 也在异步命令完成后复查终态，防止迟到回复覆盖故障状态；新增原生队列隔离及 ViewModel 延迟命令测试。

## 应用内测试

Debug 专用入口由 `build/testing/test-app-recovery.ps1` 驱动；Release 不包含故障注入与测试像素读回代码。

| 场景 | 验证 | 运动测试图案 | LG HDR 样片 |
| --- | --- | --- | --- |
| playing | 故障后图形代次增加、媒体/轨道不变、进度和 Present 继续；测试图案像素继续变化 | 通过 | 通过 |
| paused | 片中暂停画面；等待 seek 结束且位置稳定，比较实际原生位置及呈现像素 | 通过，10 秒不变，采样差异 0 | 通过，29.9966333333 秒不变，采样差异 0 |
| immediate-failure | 第一次重建后的首帧再次抛故障，页面错误可见 | 通过 | 通过 |
| rebuild-failure | 创建设备失败后页面错误可见，恢复租约与关闭流程完成 | 通过 | 通过 |
| resize | 真正的 AppWindow.Resize → WinUI SizeChanged → PlayerPage → 渲染器恢复，成功后无遗留错误 | 通过 | 通过 |

十次应用故障测试均通过终态稳定性检查，并完成页面关闭、渲染资源释放及 mpv 会话销毁，进程退出码为 0。像素采样在实际呈现前由 GPU staging texture 读回，仅在成功 Present 后完成采样请求。动态图案检查避免只靠时钟/Present 计数漏掉冻结画面。暂停验收要求位置差小于 1ms、RGB 平均绝对差小于 2/255、相对差小于 2%，而不是仅在片头黑场附近比较。

最终报告目录：`artifacts/app-recovery/reaudit-final-pattern/`、`artifacts/app-recovery/reaudit-final-lg/`。原始 JSON 包含状态、图形代次、帧数、输出配置、解码信息、位置、像素差异及 `TerminalStateStable`；这些本机产物被 Git 忽略。先前失败报告保留，不覆盖或移除。

复现命令（仓库根目录、交互式 Windows 桌面）：

```powershell
pwsh -NoProfile -File build/testing/test-app-recovery.ps1
pwsh -NoProfile -File build/testing/test-app-recovery.ps1 -NoBuild -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
pwsh -NoProfile -File build/testing/test-app-recovery.ps1 -NoBuild -Mode playback -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
```

## 样片与回归结果

- 样片：491,561,344 字节，约 74.975 秒；HEVC Main10、3840×2160、60000/1001 fps、10-bit、PQ / BT.2020。
- 样片 SHA-256：`244CA59FCA88681DB695800D9A7DE8E36BECED802955C94F797C82C4248221EA`。
- GPU：NVIDIA GeForce GTX 1060 5GB，驱动 32.0.15.8180。
- 独立原生测试确认 `d3d11va` / `d3d11-egl` / GPU `d3d11` 纹理 / `p010`，以及 3840×2160 后备缓冲中的非黑像素。最新报告：`artifacts/app-recovery/reaudit-final-hardware/hevc-hardware-report.json`；同轮也执行 AV1 软件回退及 HDR 渐变测试。
- 旧 GPU 测试在 60 帧后只取样一次；LG 开头约 1.818 秒为黑场，59.94 fps 的 60 帧仍在黑场内。现保留 60 帧门槛，额外最多等待 10 秒的非黑画面，并增加采样点密度；全黑仍判失败。
- App 测试 52/52 通过；LibMpv 测试 64/64 通过；Rendering 测试 51/51 通过。合计 167 项通过，0 失败、0 跳过；TRX 位于 `artifacts/app-recovery/reaudit-final-tests/`。
- Debug 解决方案构建及 Release 应用构建均为 0 警告、0 错误。

## 整段播放结果与未解决的性能问题

另外在实际应用执行 `playback` 模式：不注入故障、不强制重绘，仅间隔采样自然呈现的帧。约 74.975 秒样片正常到 EOF（最后位置 74.941533 秒），六次间隔画面均发生变化，D3D11 硬解模式保持不变，无页面错误，关闭成功。报告：`artifacts/app-recovery/reaudit-lg-full-playback/`。

**正常到 EOF 不代表流畅 4K60。** 该轮仅记录 1,057 次成功 Present，约 14 帧/秒；70.40 秒采样时 mpv 呈现丢帧已达 3,212，解码丢帧为 0。日志中分段最大 Render 为 96–283ms、Present 为 92–116ms，明显超出 59.94fps 的约 16.68ms 帧预算。这是实际呈现吞吐不足，不能将功能验收 Passed 解释为性能验收通过。

当前证据不足以将原因归为 GPU 算力、Debug 开销或系统呈现节流；没有完成该性能问题的修复。后续应先记录窗口可见状态、每帧平均/p95 Render/Present 和 GPU 耗时，再对比无采样运行，定位瓶颈。对应日志：`C:\Users\a1426\AppData\Local\MpvShell\logs\session-20260911-183204-9876.log`。

本次窗口显示状态为 Windows HDR 未开启，应用输出 `BGRA8 / SDR HDR 色调映射`；应用窗口表面为 1104×721，独立 GPU 测试的表面为 3840×2160。故障注入通过渲染线程抛出设备移除 HRESULT 或设备创建异常，覆盖真实应用恢复控制流程，但没有触发实际驱动重置/TDR；也未验证物理 HDR 亮度和色彩准确性。
