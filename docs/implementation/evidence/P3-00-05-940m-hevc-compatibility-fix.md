# P3-00 Intel HD 4600 HEVC 兼容修复

日期：2026-11-04。承接[修复前原设备复验](P3-00-04-940m-device-revalidation.md)。修复对象是当前源码；10 月 28 日和 11 月 3 日候选 ZIP 均未重写，不能将本次修复归入这些旧包。

后续状态：同日用户确认本设备不要求 HEVC 硬解或软件 4K60 性能；Computer Use 身份修复后 GUI 已通过，H.264 1080p30 / 720p60 常见负载稳态无丢帧。本机旧故障跟进已按此范围完成，详见 [GUI 与 H.264 验收](P3-00-06-940m-gui-and-h264-acceptance.md)。下述严格封板和工具失败为调整范围前的原始结果，保留历史。

## 定位依据

原始失败、无效转储及首次有效挂起转储全部保留。使用系统自带、Microsoft 签名有效的 `dbgeng.dll / 10.0.26100.1`，按微软 `DbgEng.h` 定义调用转储分析接口，读取首次有效转储的原生线程栈；未安装全局调试器、注册 AeDebug 或修改驱动。

- `vo` 线程停在 `igd11dxva64.dll` 的视频解码调用中，持有共享 D3D11 immediate context 的相关锁；渲染线程在 ANGLE 终止流程等待该设备。
- 托管栈显示性能探针的 `MakeParkingCurrent` 已抛异常，但原来的 `try { MakeParkingCurrent(); render.Dispose(); } finally { angle.Dispose(); }` 跳过了 mpv context 释放，进入错误的终止顺序。
- 匹配本机 .NET 10.0.14 的 PDB 后，先前事件地址 `coreclr+0x3590bf` 对应 `ProcessCLRException` 的致命异常处理路径。该地址不是最初的驱动故障位置，不能据此认定 CLR 是损坏起点。
- mpv [硬件解码文档](https://mpv.io/manual/stable/#options-hwdec) 提及 Windows Intel/HEVC 组合的崩溃风险；[hwdec-codecs](https://mpv.io/manual/stable/#options-hwdec-codecs) 专门允许对报告支持但实际故障的编码收窄硬解范围。实现同时对照仓库锁定的 v0.41.0 源码和实际属性返回类型。

以上证据支持对旧 Intel HEVC 路径实施兼容处理；没有定位 Intel 驱动内部的具体错误指令，不宣称修复了驱动本身。

## 代码修复

1. D3D11 层按实际适配器的 `VendorId=0x8086` 和 Haswell 设备族 `0x04xx / 0x0Axx / 0x0Dxx` 识别兼容范围。当前实际设备为 `0x0416 / Intel HD Graphics 4600`。现代 Intel、NVIDIA 和 AMD 不应用此策略。
2. 创建 mpv render context **之前**，在 mpv 事件线程上更新 `hwdec-codecs`，移除 HEVC。保持 `hwdec=d3d11va`、同设备 EGL GPU 互操作和其他编码的原有策略；HEVC 使用软件解码，不使用 copy-back。
3. 配置复用渲染配置锁和原有异步请求通道；视频挂起/页面交接持有播放租约时，不再次等待播放修改锁。重复配置不重复设置原生属性。
4. 性能探针即使 EGL 切换失败，也先尝试释放 mpv render context，再释放 ANGLE/D3D11；原始失败继续向上传播。性能日志即时落盘。
5. 应用播放探针仅将 `MPV_ERROR_PROPERTY_UNAVAILABLE (-10)` 记为可缺省的计数，EOF 后的 `hwdec-current` 返回 null。其他请求错误继续失败。短片按剩余时长分配采样间隔，仍要求至少两次画面变化。
6. 新增设备族、原生编码列表与幂等、短片采样测试，以及独立的 Main10 软件回退、TS 软件回退跳转验收。原有 HEVC 必须硬解、TS 硬软对照和自然播放断言不放宽。

## 原参数对照

`guarded-original/` 沿用原 4K60 HEVC、3840×2160、vsync 1、预热 3 秒、稳态 20 秒、无窗口内读回、`RequireHardwareDecode=true`。运行完成，媒体推进 20.066 秒，窗口墙钟 19.992 秒，呈现 248 帧，约 12.42 fps，实际解码 `Software`，非黑像素检查通过，render context 释放完成，无原生崩溃或挂起。

原用例随后因 `IsGpuResident=false` 正常断言失败，退出 1；保留这一结果，不登记为硬解通过。兼容处理解决稳定性，**不表示旧设备达到 4K60 吞吐，也不表示已实际验证 NVIDIA 940M 渲染路径**。

## 最终验证

原始材料根目录为 `artifacts/p3-00/fix-20261104-940m/`。使用同一份最终源码完成以下验证；构建与应用运行之后再次核验输入身份。

| 验证 | Debug | Release | 证据 / 边界 |
|---|---|---|---|
| 解决方案构建 | 0 警告 / 0 错误 | 0 警告 / 0 错误 | `final-baseline/*-build.log` |
| 默认全量 | 538 通过 / 0 失败 / 11 条件跳过 | 538 / 0 / 11 | 每轮 4 份 TRX |
| 显式兼容清单 | 547 通过 / 0 失败 / 2 条件跳过 | 547 / 0 / 2 | 两项必须 HEVC 硬解的测试未启用；其原断言保留 |
| Main10 软件回退 | 62 帧、非黑、10-bit、实际 Software | 61 帧、非黑、10-bit、实际 Software | `*-explicit-hardware/hevc-software-fallback-hardware-report.json`；不表示流畅度达标 |
| TS 软件回退 | 8 个跳转目标及顺播无参考帧错误 | 同左 | `*-explicit-hardware/ts-seek-software-fallback.json`；实际 decoder 为 `no` |

`final-baseline/closure-verification.json` 的 `buildInputsUnchanged=true`，192 项构建输入及 22 处素材/清单哈希一致。六条构建/测试命令退出码均为 0，但 runner 要求显式轮零跳过，因此 **`allCommandsPassed=false`，严格封板未通过**。本轮登记的是兼容条件下实际执行测试零失败，不能写成原硬解要求全部满足。逐项跳过清单另存 `compatibility-summary.json`；没有修改 runner 放行规则。

同一 Debug 构建的真实 WinUI 应用探针：

- `final-app-recovery/`：11 个播放/暂停、故障恢复、页面重建、缩放、恢复中换片和加载/恢复中关闭场景全部通过，进程均退出 0，`ShutdownCompleted=true`。
- `final-app-h264/`：12.032 秒多轨短片自然到 EOF，3 次画面变化，实际 `d3d11va` / GPU 纹理路径，约 23.98 fps；会话、渲染器释放，退出 0，14.71 秒完成。
- `final-app-hevc/`：原 30 秒 4K60 HEVC 片源自然到 EOF，5 次画面变化、实际软件解码；会话、渲染器释放，退出 0，33.33 秒完成。1382×903 表面呈现约 19.74 fps，已观察大量呈现丢帧，**不是 4K60 流畅播放通过**，与原 3840×2160 表面的 12.42 fps 不混写。

以上应用运行均实际选择 Intel HD 4600、非 WARP、未启用调试层。未执行 NVIDIA 940M 独显渲染路径验收。测试后删除的四条最近记录均与本轮固定素材逐项匹配，恢复开工前历史文件不存在的状态，见 `history-restoration.json`；GUI 诊断测试进程按实际路径核对后清理。

收尾再次读取 192 项构建输入、22 处素材/清单及两个本地原候选 ZIP，哈希均匹配；无遗留 `MpvShell.App/testhost` 进程，`git diff --check` 通过，见 `final-verification.json`。首次收尾脚本的属性比较写法将 `actual` 当作字面量，误记三个一致性字段为 false；错误结果另存 `verification-comparison-error.json`，修正比较并重新读取全部输入后才登记最终结果。原始材料逐文件 SHA-256 存于 `evidence-sha256.json`。

兼容清单由原 `media-inputs.json` 派生，保留全部 22 处素材/清单及其哈希。明确不启用本设备不适用的 HEVC Main10 必须硬解与 TS 硬软对照；通过独立软件回退用例验证同一 Main10、TS 素材，性能场景明确不要求硬解。清单差异不用于改写原严格场景的结果。

中间失败同样保留：`policy-tests/` 是首次新测试读取属性类型错误；`baseline-original-requirements/` 的默认轮暴露交接期间配置等待播放租约，536 通过、2 失败、9 跳过，已修正；该轮取消标记和剩余未完成项保留。随后 `targeted-recovery/` 的 4 项交接测试和原生兼容配置测试通过。`baseline-compatibility/` 完成 Debug 默认 538 通过后，为补充独立 TS 软件跳转验收而停止；最终结果另记，不拼接为完整轮。

## Computer Use 边界

本轮继续使用提供的 `@oai/sky`，刷新清单、重新初始化连接，尝试直接读取返回的唯一窗口；仍报告窗口归属为 `SwitchDesktop.App.exe` 并拒绝读取。为排查继承身份，曾试验进程级和窗口级 AppUserModelID；均未改变连接错误，相关产品改动已全部撤回。最终构建又用清单返回的实际 `process:` 标识选择窗口，并通过 `sky.launch_app` 重新启动，读取仍失败，见 `final-gui/{result,sky-launch-result}.json`：

```text
window id 33424902 no longer belongs to {6D809377-6AF0-444B-8957-A3773F02200E}\Switch Desktop\SwitchDesktop.App.exe; current owner is {6D809377-6AF0-444B-8957-A3773F02200E}\Switch Desktop\SwitchDesktop.App.exe
```

未取得本轮截图、控件树或成功的 UI 输入。应用内探针和源码测试不替代 Computer Use GUI 验收；P3-00 的这部分保持待验证，不能把旧候选或本轮源码写成 GUI 通过。
