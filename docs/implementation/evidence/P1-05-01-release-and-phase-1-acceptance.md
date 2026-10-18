# P1-05 发布可复现与 Phase 1 验收

日期：2026-10-18。结论：**P1-05 通过，Phase 1 在计划声明范围内完成**。最终构建/显式媒体回归、原生闭包、许可与源码快照、最终目录运行/退出及 ZIP 校验均完成；PERF-02 和既有硬件/长期验证范围独立保留。

## 1. 输入与产物身份

- 基线提交：`d7522cba3e06c75ead25c91f700d10d9d5717ee5`；构建时工作区包含尚未提交的 P1-04 修复/测试及本次发布脚本，`sourceDirty=true`。不能用基线提交单独重建本包。
- 最终发布目录：`artifacts/phase-1/MpvShell-phase1-20261018-120914-200896/`；Release / win-x64，.NET 与 WinUI 自包含，PDB、原生/托管许可、固定样片及日志入口齐全。
- 最终 ZIP：[本机发布包](../../../artifacts/phase-1/MpvShell-phase1-20261018-120914-200896.zip)，134,822,757 字节；SHA-256 `BAD4D64F8157F71A12E78651822378F370383475E8220B453FB26ACC68BA90D7`。691 个文件全部按长度与 SHA-256 核验，包外 `.sha256` 覆盖整个 ZIP；包内内容清单不包含自身，ZIP 校验包含内容清单。
- 源码快照：同级 `MpvShell-phase1-20261018-120914-200896-source.zip`，257 个文件，SHA-256 `70FDABC9B0613E4FE01F6F195BBB124A65B2F9D3919C833AABF4BF4323D66BB5`；逐条长度与 SHA-256 已核验。
- 包内 `source-hashes.json` SHA-256：`0468ABC6F626F4EE07C6865899A80740F136A539C8D5DE3E0F1BB985B6126F51`。冻结时源文件全部与快照一致；本验收记录与最终进度登记在冻结后回填，不改变构建输入。
- 构建工具：.NET SDK 10.0.402、PowerShell 7.6.5。自包含发布实际带 .NET 10.0.13；版本与 NuGet 包哈希以包内 runtimeconfig 和 `ThirdParty/nuget-license-origins.json` 为准。
- 原始证据：`artifacts/p1-05/`（构建/回归日志、16 份 TRX、媒体/像素/TS/性能 JSON、环境信息、发布日志及 GUI 启动记录）。

## 2. 最终构建与回归

| 配置 | 构建 | 默认回归 | 显式素材回归 |
|---|---|---|---|
| Debug x64 | 0 警告、0 错误 | 298 通过、0 失败、9 跳过 | 307 通过、0 失败、0 跳过 |
| Release x64 | 0 警告、0 错误 | 298 通过、0 失败、9 跳过 | 307 通过、0 失败、0 跳过 |

四测试程序集：Abstractions 2、LibMpv 126、App 102、Rendering 77。默认跳过为 P1-04 四项及 HEVC/AV1/PQ/TS/性能五项，显式运行时全部实际执行。本轮构建/回归没有失败重跑；各历史修复的失败记录仍保留在原工作包证据中。

执行入口：`artifacts/p1-05/run-regression.ps1`，按 Debug/Release 顺序执行以下命令；每种配置先清除 `MPVSHELL_TEST_*` 跑默认配置，再按 `regression-inputs.json` 设置显式输入重跑：

```powershell
dotnet build mpv-winui.slnx -c <Debug或Release> -p:Platform=x64 --no-restore
dotnet test mpv-winui.slnx -c <Debug或Release> -p:Platform=x64 --no-build --no-restore --logger trx --results-directory <独立目录>
```

显式输入与结果：

- P1-04 使用 `artifacts/p1-04/media-final/`，清单 SHA-256 `B8ED0E000C4663A112E1E6548970EF4E0BAFAF7F8495F769B69C827392FF5FA9`。视频 HLS、404 重载、缓冲恢复、多轨及换媒体信息均通过；SRT 下部变化 504 像素/上部 0，ASS 上部 2,288/下部 0。原 P1-04 的 9 项产品/测试/素材脚本哈希核对一致。
- 三份 4K 样片由 `prepare-hdr-4k-media.ps1 -OutputDirectory artifacts/p1-05/media` 重新生成；FFprobe 元数据和全片 CPU 解码通过。HEVC SHA-256 `E6C53385F6D49EC8A76861D109A1084545656EA2EFC897E7CC07E9716E815B65`；AV1 `83B88042044DC5FCCE8660827F90FAA58EFD6FE580F7F67B53D469AFF3C6C3F3`；PQ 渐变 `BFBEBB8C19086D3DF9155192DC4B18E43E886C80350BAAF707717998786A7876`。
- HEVC Main10 实际使用 GPU 常驻 D3D11VA；GTX 1060 上 AV1 实际软件回退并显示非黑像素；PQ 绝对亮度与 SDR tone-map 像素检查通过。这些不等同于显示器色准或全部硬件兼容认证。
- TS 使用既有 `hevc-opengop-seek-720p60.ts`，目标 `2,4,7,11,16,21,25,9` 秒、各变体顺播 20 秒。两配置产品硬解/软解跳转错误均 0；关闭关键帧起读的对照各产生 192 组错误，所有变体顺播错误 0。
- 性能输入为既有 HEVC SDR 4K60 / 30 秒样片，4K 离屏表面、D3D11VA、vsync=1、预热 3 秒/稳态 20 秒、稳态不读回。Debug/Release 均约 **10.67 fps、989 次 VO 掉帧、0 解码掉帧**，Present 平均约 90.6 ms。报告状态是“已测量，未判定”，没有最小 fps 通过断言；测试通过只表示测量/媒体推进/硬解链路完成，**不能宣称 4K60 流畅或关闭 PERF-02**。

本机为 Windows 11 10.0.26300、GTX 1060 5GB / 驱动 32.0.15.8266，活动 RDP 会话，系统同时列出 GameViewer 和 Remote Display Adapter；功能报告实际 ANGLE 适配器为 GTX 1060。低帧率原因未在本包归因，不将其归因于采样（本次稳态没有读回）。既有用户本地显示场景的结论不自动扩展到本次环境。

## 3. 原生闭包、许可与复现流程

候选目录四原生 DLL 与锁文件一致，均为 x64；加载、Client API 2.5、会话创建/初始化/销毁通过。底层脚本在临时发布目录执行三次会话烟雾，移入最终候选目录后再次执行三次。无 `mpv.exe` 或旧 Sidecar/VideoHost DLL。

| 文件 | SHA-256 |
|---|---|
| libmpv-2.dll | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| d3dcompiler_47.dll | `E407B9FFADEF47E87994DC488214F75405CF04EE99589F2315FC7AB99FD1DFE2` |
| libGLESv2.dll | `55ECF05D47B4CCFCF91B4F5FE0F21ED3FCE2584B6CC54E4F6855A65142F32D3C` |
| libEGL.dll | `8B912D254D5FC8755840B6FD1E404F36E466521FD63F68255528CD7F101909D7` |

mpv v0.41.0 / `41f6a645068483470267271e1d09966ca3b9f413`，ANGLE chromium/7977 / `736ed80c7552a4b267bd54a282b971aa4555cb3e`。原生源码锁、工具版本/哈希、Meson 包装器、6 个补丁及重建说明随源码快照与包内 `SourceBuild/` 提供；本轮复用锁定 DLL，没有重新编译原生依赖。

35 份原生许可原文逐项匹配 `license-origins.json`。本次补齐旧装配流程未统一携带的 .NET/WinUI/托管许可：26 个 NuGet/运行时包的 66 份许可、通知和 nuspec 均有 SHA-256。SharpGen/Vortice 许可按 nuspec 内的精确上游提交补充，来源及哈希固定在 `build/testing/licenses/origins.json`；Windows SDK 构建工具使用仓库已锁定的 SDK 许可原文并注明范围。这是许可文件/来源的技术核验，不扩大实际发布 DLL 集合。

```powershell
pwsh -File build/testing/publish-phase1.ps1 -HdrMediaDirectory artifacts/p1-05/media -P104MediaDirectory artifacts/p1-04/media-final
# 从返回的目录运行并验收；保存日志到包外后：
pwsh -File build/testing/seal-phase1-package.ps1 -PackageDirectory <返回的独立目录>
```

复现使用源码快照、清单所指 NuGet/运行时版本与哈希、已锁定原生 DLL，按根 README 构建。需要重建原生 DLL 时在完整快照的 `build/native/` 运行说明中的步骤。素材生成工具为 FFmpeg/FFprobe 8.1.2，完整命令、工具哈希及输入哈希保存在媒体清单；工具不随运行包分发。没有证明、也不承诺重新构建的二进制逐字节一致。

## 4. 最终目录运行

使用系统 Windows PowerShell 5.1 的包内 `Start-Debug.ps1` 启动 `TestMedia/p1-04/multitrack.mkv`。会话 `20261018-121004-daece7342c7d4735b8db600885c67f3f` 的日志确认 H.264/AAC D3D11VA、WASAPI、12.032 秒正常 EOF；这不替代听音或可见像素验收。

首次 Computer Use 截图全黑，激活失败：`GetCursorPos failed: 拒绝访问。 (0x80070005)`。用户恢复可交互桌面后，控件操作恢复，重新打开同一多轨素材到 EOF，点击窗口关闭按钮正常退出；释放日志确认 render context → EGL/SwapChain/D3D11 → mpv core → 主窗口关闭。

第二次运行包内 `hevc-main10-hdr10-4k30.mkv`，会话 `20261018-121551-22ea0b8e090a418b8fcf921e1b38b7a1`：HEVC Main10 / P010 / D3D11VA 常驻硬解到 8 秒 EOF，保留片源信息；Windows HDR 关闭，实际输出 **SDR HDR 色调映射**，表面 1658×1084 / BGRA8，D3D11 调试层关闭。播放器置前后取得完整灰阶/亮度测试图及 XAML 控件截图 `artifacts/p1-05/gui-hevc-eof.png`（SHA-256 `5A1B92B6093F445D650F07B8A76854962FC09DD7BC9CA7750DA0B0376B41166E`）。这确认最终目录可显示画面，不代表 HDR 显示输出或色准验收。

随后输入再次被拒绝，恢复重试仍失败；用户手动正常关闭第二个窗口，并说明此前最小化了微软远程桌面。两次 `run-info.json` 均为 `Exited`、退出码 **0**，stderr 为空，均有 EOF 和按序资源释放标记；第一轮由工具点击关闭，第二轮由用户关闭，不将两者混写为全自动验收。RDP 锁屏/最小化的黑截图与输入失败保留为环境历史。

`artifacts/p1-05/gui-run-summary.json` 保存两个退出记录及日志哈希。Windows PowerShell 5.1 执行包内 `Collect-Logs.ps1` 成功收集两次运行，生成 `artifacts/phase-1/MpvShell-Phase1-Logs-20261018-122013-1d3c0849.zip`；原始日志移至 `artifacts/p1-05/gui-logs/`。最终交付 ZIP 不含本机运行日志。日志统计段包含 EOF 等待和重播（第一轮一个段长 281 秒），不可当作连续播放 fps；短片第二轮未达到周期统计的 300 帧，也不能只用首帧统计估算吞吐。

## 5. 阶段验收映射

| 工作包 | 已有依据 | 当前状态 |
|---|---|---|
| P1-00 基线与入口 | [基线记录](P1-00-01-baseline-and-release-entry.md) | 已通过 |
| P1-01 固定媒体与性能场景 | [场景记录](P1-01-01-fixed-media-and-performance-scenarios.md)、[用户复测收口](P1-01-05-gtx1060-user-acceptance.md) | 已通过；PERF-02 独立保留 |
| P1-02 状态、错误与命令 | [验收汇总](P1-02-06-p1-02-acceptance.md) | 已通过；本次全量复验无失败 |
| P1-03 生命周期 | [验收汇总](P1-03-03-p1-03-acceptance.md) | 已通过；真实驱动重置不在注入证据范围 |
| P1-04 视频 HLS、字幕和信息 | [验收记录](P1-04-01-video-hls-subtitles-and-media-information.md) | 已通过；本次两配置显式复验 |
| P1-05 最终发布 | 本记录及 `artifacts/p1-05/` | 通过；构建/默认与显式回归、闭包、许可、源快照、最终目录播放退出、日志收集和 ZIP 核验完成 |

P1-05 计划的五条验收分别对应本记录 §2（构建与默认/显式结果）、§3（四 DLL/会话与许可/复现）、§4（最终目录实际运行）及本表（前序证据）；[唯一发布入口](../release-status.md)登记本包的精确身份。未发现本次声明范围内未解决的发布阻塞。发布包来源以构建时的基线提交及源码快照为准；后续源码提交不改变本包的构建身份，历史外机包身份另行记录。

跨屏、长期稳定性、真实触屏、其他 DPI、Intel/AMD、HDR 仪器测量、其他 HLS/字幕格式继续按既有范围保留未验证。完整 UI、倍速、持久化最近打开等仍属于后续产品阶段。
