# P1-03：应用内生命周期场景记录

- 日期：2026-10-16。
- 输入：`d5b01a4` 的 x64 Debug 应用（构建产物时间 11:59:37；会话日志版本行 `1.0.0+d5b01a4453b2adf7445383c70bf7701730038aec`），运行前加入未提交的探针报告字段 `TracksBefore`（已随本次收口提交）；报告含该字段即证明运行所用构建包含此改动。
- 范围：真实页面卸载/重新挂载（播放/暂停）、恢复中关闭、加载中关闭、恢复中换媒体、重复故障（`immediate-failure`、`rebuild-failure`）与既有 5 场景重跑；默认合成素材、4K60 HEVC 硬解素材与含音轨 TS 素材。
- 原始记录：`artifacts/p1-03/app-full-final/`（默认 10 场景，一次调用）、`artifacts/p1-03/app-4k-final/`、`artifacts/p1-03/app-ts-final/`（各两次单场景调用）；每个场景含独立应用进程、JSON 报告与经 `MPVSHELL_LOG_DIRECTORY` 重定向的会话日志。

## 1. 入口、判定与环境

运行入口为恢复脚本（PowerShell 7）；各次调用的模式、素材与报告目录如下（参数值取自各 `summary-*.json`，运行所用可执行文件为 `src/MpvShell.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/MpvShell.App.exe`）：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -ReportDirectory artifacts/p1-03/app-full-final
pwsh -File build/testing/test-app-recovery.ps1 -Mode page-rebuild-playing -MediaPath artifacts/hdr-4k/media/hevc-main-sdr-4k60-30s.mkv -ReportDirectory artifacts/p1-03/app-4k-final
pwsh -File build/testing/test-app-recovery.ps1 -Mode page-rebuild-paused -MediaPath artifacts/hdr-4k/media/hevc-main-sdr-4k60-30s.mkv -ReportDirectory artifacts/p1-03/app-4k-final
pwsh -File build/testing/test-app-recovery.ps1 -Mode page-rebuild-playing -MediaPath artifacts/hdr-4k/media/hevc-opengop-seek-720p60.ts -ReportDirectory artifacts/p1-03/app-ts-final
pwsh -File build/testing/test-app-recovery.ps1 -Mode page-rebuild-paused -MediaPath artifacts/hdr-4k/media/hevc-opengop-seek-720p60.ts -ReportDirectory artifacts/p1-03/app-ts-final
```

判定：脚本要求 `Status="Passed"`、`ShutdownCompleted=true`、进程退出码 0，并核对 `SessionReleased`、`RendererReleased`、`ShutdownRepeatSafe` 以及会话日志中的“会话已销毁”“渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放”标记。

环境：GTX 1060 5GB / Windows 11（10.0.26300）/ .NET 10.0.13；ANGLE D3D11；输出 SDR · 8-bit，Windows HDR 未开启；D3D11 调试层未启用；窗口 1104×721。素材：脚本生成的 64×64、30 fps、20 s Y4M（上红下蓝 + 运动白标，另有换色版用于换媒体场景）；`hevc-main-sdr-4k60-30s.mkv`（HEVC 3840×2160@60、8 bit、SDR）；`hevc-opengop-seek-720p60.ts`（HEVC 开放 GOP、1280×720@60、AAC 正弦、30 s）。后两者报告均记录 D3D11 硬解、解码/丢弃丢帧 0。

## 2. 默认 10 场景结果（`app-full-final`）

| 场景 | 时长 | 关键报告值 | 结果 |
|---|---|---|---|
| playing | 4.167 s | 注入 2 次故障；重建后位置 1.000 → 1.567 s 继续前进 | 通过 |
| paused | 3.905 s | 重建前后暂停位置 10.000 s（差 <0.001 s）；像素差 0 | 通过 |
| immediate-failure | 3.427 s | 重建后再次故障，到达终态并保持稳定 | 通过 |
| rebuild-failure | 3.407 s | 图形设备重建失败进入终态 | 通过 |
| resize | 4.025 s | 窗口尺寸变化（1104×721 → 1136×745）后恢复呈现 | 通过 |
| page-rebuild-playing | 4.089 s | 真实卸载/重挂载；`OldRendererReleased=true`；卸载窗口保持受控暂停；重建后像素变化 0.0323（>0.001）；场景累计呈现 50 帧 | 通过 |
| page-rebuild-paused | 4.110 s | 真实卸载/重挂载；暂停位置 10.000 s 前后一致（差 <0.001 s）；像素差 0（同一帧） | 通过 |
| close-during-recovery | 4.994 s | 重建步骤显式持有 2.000 s 期间请求关闭；`ShutdownSeconds=2.169`；会话与渲染器均已释放 | 通过 |
| close-during-load | 1.558 s | 加载受理后立即关闭（`LoadAccepted=true`；地址为脚本自建的本机停滞端点，见 `LoadUrl`）；`ShutdownSeconds=0.075` | 通过 |
| recovery-media-switch | 6.013 s | 恢复租约持有期间换媒体：换入文件成为最终媒体、无错误提示；像素变化 0.0430 | 通过 |

全部场景：`SessionReleased`/`RendererReleased`/`ShutdownRepeatSafe` 均为 true、退出码 0；会话日志均含释放（“渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放。”）与销毁（“会话已销毁。”）标记；页面重建场景另含挂起（“视频已挂起等待重新挂载；播放修改租约保留到恢复或会话销毁。”）与恢复（“重新挂载后已恢复原视频轨、播放位置与暂停状态。”）标记。

## 3. 4K 与 TS 素材补充

| 素材 | 场景 | 关键报告值 | 结果 |
|---|---|---|---|
| 4K60 HEVC 硬解 | page-rebuild-playing | 卸载窗口受控暂停；重建后像素变化 0.0033（>0.001）；场景累计呈现 65 帧 | 通过 |
| 4K60 HEVC 硬解 | page-rebuild-paused | 暂停位置 15.000 s 前后一致（差 <0.001 s）；像素差 0 | 通过 |
| 含音轨 TS | page-rebuild-playing | `TracksBefore=[1]`（音频轨）重建后列表恒等；像素变化 0.0261；场景累计呈现 69 帧 | 通过 |
| 含音轨 TS | page-rebuild-paused | 暂停位置 15.021 s 前后一致（差 <0.001 s）；像素差 0；`TracksBefore=[1]` 重建后恒等 | 通过 |

说明：`vm.Tracks` 按既有约定只含音频/字幕轨（视频轨经原生 `vid` 属性单独校验），因此合成与 4K 素材（无音轨）记录为 `[]`，TS 素材记录音频轨 `[1]`；重建后探针断言两个清单逐项与原值相等，断言失败会使该场景报告非 `Passed`。

## 4. 关键阈值

- 卸载窗口：媒体必须保持暂停（`DetachedPause=true`），位置偏差 <0.2 s。
- 重建后暂停：原生位置差 <0.001 s；画面差 <2/255 且相对差 <2%（实测均为 0）。
- 重建后播放：间隔 600 ms 两次采样的像素变化 >0.001，防止时间前进而画面冻结。
- 关闭：`ShutdownSeconds` 为关闭请求到窗口关闭的耗时；恢复中关闭需等待重建持有的生命周期锁与租约，实测 2.169 s（在持有 2.000 s 的窗口内完成）。

## 5. 限制

- 故障均为注入异常，不触发操作系统 GPU 超时检测与真实驱动重置（TDR）。
- 4K 素材为 SDR 编码、SDR 输出；HDR 显示输出与仪器测量不在此记录范围。
- 使用默认窗口 1104×721 与 GTX 1060；未做人工目视验收，未重新发布。
