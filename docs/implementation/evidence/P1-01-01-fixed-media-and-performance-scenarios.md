# P1-01：固定媒体回归与性能场景复核

| 字段 | 记录 |
|---|---|
| 日期 | 2026-09-21 起，北京时间；本记录随工作包推进更新 |
| 基线 | `main` / `8a4829c`（P1-00 文档提交）；开始时工作区干净 |
| 状态 | **进行中。** 素材清单、场景清单、测量工具与判定规则已建立；场景数据尚未在目标机上执行，本文第 5 节全部为"待执行" |
| 范围 | [Phase 1 计划 P1-01](../phase-1-plan.md#p1-01固定媒体回归与性能场景复核)：固定输入可再生成、功能与性能分开报告、PERF-01/PERF-02 复核、必要修复 |
| 不在范围 | 跨屏、长期稳定性、触屏、其他 DPI 与 Intel/AMD 硬件矩阵仍按既有决定延后；短时性能复核不等于长期稳定性验收 |

## 1. 本轮改动摘要

| 类别 | 文件 | 目的 |
|---|---|---|
| 渲染统计 | `src/MpvShell.Rendering.WinUI/RenderStatistics.cs`、`RenderDeviceInfo.cs` | 渲染线程内逐帧累计 Render / Present / 回调至呈现的均值、中位、p95、最大值与呈现 fps；无每帧分配、无 CPU 读回。分位数按最近秩法，不插值 |
| 渲染器 | `D3D11VideoSurfaceRenderer.cs` | 新增 `GetStatisticsAsync(reset)` 与 `GetDeviceInfoAsync()`；每 300 帧日志改为输出本段完整分布与本段 fps，替换原来只有最大值的日志 |
| 调试层 | `D3D11DeviceManager.cs` | 记录设备是否实际带 D3D11 调试层、是否 WARP、适配器名；Debug 构建可用 `MPVSHELL_D3D11_DEBUG_LAYER=0` 关闭调试层做同构建对照。Release 行为不变 |
| SwapChain | `CompositionSwapChain.cs` | `Present(uint syncInterval)` 重载，产品路径仍固定为 1；测量对照可用 0 |
| 离屏测量 | `tests/MpvShell.Rendering.WinUI.Tests/PlaybackPerformanceTests.cs` | 素材驱动的自然播放测量（`MPVSHELL_TEST_PERF_*`），预热 + 稳态窗口，窗口内无读回；含不依赖外部素材的链路自检 |
| 单元测试 | `RenderStatisticsCollectorTests.cs` | 计数、分位数、容量耗尽、非法输入与调试层开关解析 |
| 应用内测量 | `src/MpvShell.App/Diagnostics/AppRecoveryProbe.cs`、`build/testing/test-app-recovery.ps1` | 新增 `performance` 模式（无采样）；`playback` 模式（有采样）现在同样输出渲染统计与 mpv 计数；脚本新增 `-WarmupSeconds`、`-SteadySeconds`、`-D3D11DebugLayer Off`、`-TimeoutSeconds` |
| 素材 | `build/testing/prepare-perf-media.ps1` | 生成 4K60 HDR10 与 4K60 SDR 各 30 秒的固定合成样片及 `perf-media-manifest.json` |

改动没有触碰播放控制契约、恢复流程或输出选择逻辑；`Present()` 无参重载仍等价于原实现。

## 2. 固定素材清单

所有素材都在本机 `artifacts/` 下生成，不进入 Git；哈希以各自清单 JSON 中的 `sha256` 为准，本表不复制哈希值以免与清单脱节。清单文件在生成时写出，再次运行生成脚本只做哈希校验。

| 素材 | 生成方式 | 编码 / 规格 | 适用测试 | 环境变量 |
|---|---|---|---|---|
| `hevc-main10-hdr10-4k30.mkv` | `pwsh -File build/testing/prepare-hdr-4k-media.ps1`；清单 `artifacts/hdr-4k/media/media-manifest.json` | HEVC Main10、3840×2160@30、8 s、PQ/BT.2020、HDR10 元数据、横向滚动 | `HardwareDecodeIntegrationTests`（D3D11VA / d3d11-egl / P010 功能验收） | `MPVSHELL_TEST_HEVC_MEDIA` |
| `av1-main10-sdr-4k30.mkv` | 同上 | AV1 Main 10-bit、3840×2160@30、8 s、BT.709 | `HardwareDecodeIntegrationTests`（硬解或明确软件回退） | `MPVSHELL_TEST_AV1_MEDIA` |
| `pq-gradient-10bit-4k.mkv` | 同上 | FFV1 无损 10-bit PQ 灰阶、1 fps、8 s | `HdrGradientIntegrationTests`（像素通路） | `MPVSHELL_TEST_HDR_GRADIENT_MEDIA` |
| `hevc-opengop-seek-720p60.ts` | `pwsh -File build/testing/prepare-ts-seek-media.ps1`；清单 `hevc-opengop-seek-720p60.ts.manifest.json` | HEVC 开放 GOP、1280×720@60、30 s、MPEG-TS、AAC 正弦 | `TsSeekDecodePathComparisonTests` | `MPVSHELL_TEST_TS_MEDIA`、`MPVSHELL_TEST_TS_SEEK_TARGETS`、`MPVSHELL_TEST_TS_SEQUENTIAL_SECONDS` |
| `hevc-main10-hdr10-4k60-30s.mkv` | **新增** `pwsh -File build/testing/prepare-perf-media.ps1`；清单 `perf-media-manifest.json` | HEVC Main10、3840×2160@60、30 s、PQ/BT.2020 标记 + HDR10 元数据、GOP 120 | `PlaybackPerformanceTests`、应用内 `performance` / `playback` 模式 | `MPVSHELL_TEST_PERF_MEDIA` |
| `hevc-main-sdr-4k60-30s.mkv` | **新增** 同上 | HEVC Main 8-bit、3840×2160@60、30 s、BT.709、同图案同 GOP | 同上（SDR 对照） | `MPVSHELL_TEST_PERF_MEDIA` |
| 64×64 Y4M（红蓝 / 黑白） | 测试内生成 `NativeRenderIntegrationTests.WriteColorVideo`；脚本内生成 `test-app-recovery.ps1` | 2 fps 或 30 fps 原始 YUV420 | 原生渲染、连续性、恢复、性能链路自检 | 无（自动生成到临时目录） |
| WAV / HTTP / HLS | 测试内生成：`MpvPlayerSessionTests.WriteWave`、`HttpMediaServer`、`HlsMediaServer`（ADTS AAC 分片） | 音频 | `LibMpvHttpTests`、`LibMpvHlsTests` | 无 |
| LG 4K HDR 演示 TS（私人） | 用户本机文件，不入 Git | 3840×2160@59.94、HEVC Main10 HDR10、约 75 s | PERF-01 历史场景的原始输入；只能作为补充对照 | `MPVSHELL_TEST_PERF_MEDIA` |

FFmpeg 固定为 gyan `8.1.2-essentials_build`，工具哈希写入清单；不同工具版本或 CPU 的有损编码结果可能不同，"可再生成"不等于跨机器字节一致。新增 4K60 样片为 `testsrc2` 图案：HDR10 样片按 PQ/BT.2020 标记并写入静态元数据以驱动同一色调映射着色器路径，但像素不是按 PQ 公式生成的真实亮度，不用于亮度或色彩准确性判断；码率和运动复杂度低于真实电影，只能证明链路吞吐。

## 3. 测量方法与口径

| 项目 | 口径 |
|---|---|
| 呈现 fps | 稳态窗口内渲染线程实际完成 `Present` 的帧数除以墙钟秒数；不是 mpv 估计的源帧率 |
| 呈现丢帧 | mpv `frame-drop-count`（VO 丢帧）窗口首尾差值 |
| 解码丢帧 | mpv `decoder-frame-drop-count` 窗口首尾差值 |
| 迟到 / 错时 | mpv `vo-delayed-frame-count`、`mistimed-frame-count` 差值，只记录不判定 |
| Render 耗时 | `mpv_render_context_render` 开始到 `Present` 开始（含 `PreparePresent` 的 `glFlush` 与切换 parking surface） |
| Present 耗时 | `IDXGISwapChain::Present` 调用耗时；sync interval 1 时包含 vsync 等待 |
| 回调至呈现 | mpv 更新回调到该帧 `Present` 返回；只对能对应到回调的帧统计 |
| 预热 | 默认 3 s，首帧、着色器编译、解码器初始化不计入窗口 |
| 稳态窗口 | 离屏默认 20 s，应用内默认 30 s；窗口在媒体位置越过预热秒数时开始，到达预热 + 窗口秒数或 EOF 时结束 |
| 采样 | "无采样"：窗口内零次 CPU 读回、零次强制重绘；"有采样"：按间隔做与 `playback` 模式相同的 staging 拷贝 + Map 读回 |
| 环境登记 | 每份报告记录适配器、是否 D3D11 调试层、是否 WARP、ANGLE 描述、输出格式、表面尺寸、构建配置、libmpv SHA-256、素材 SHA-256、OS 版本 |

## 4. 性能场景清单与判定规则

每个场景在执行前写明目标帧率、稳态区间和判定规则；执行后只把实测填入第 5 节，不改规则。规则：

- **R1 吞吐**：稳态呈现 fps ≥ 目标帧率 × 0.98，且窗口内 VO 丢帧 ≤ 窗口帧数 × 1%，解码丢帧 = 0。满足则该场景"通过"。
- **R2 未达标分类**：不满足 R1 时必须归入下列之一，附同条件对照数据：(a) 实现/配置缺陷——修复后同条件复测；(b) 测试调度或采样开销——"无采样 vs 有采样"或"调试层开/关"对照能解释差异，且无采样自然播放满足 R1；(c) 硬件限制——Release、无调试层、无采样仍不满足 R1，且 `Render` p95 已接近或超过帧预算。未归类前保持"待验证"。
- **R3 独立性**：其他 GPU、其他输出路径（例如 RTX 3070 的 PQ 输出）的通过不能替代本场景；功能测试通过不能推断性能通过。
- **R4 可比性**：对照组之间只允许改变一个变量（构建配置、调试层、采样、vsync、hwdec、输出模式）。

| 编号 | 场景 | 承接 | 输入 | 环境 | 目标帧率 | 窗口 | 运行方式 |
|---|---|---|---|---|---|---|---|
| S1 | 4K60 HDR10 → SDR 色调映射，应用内，Debug，无采样 | PERF-01 主场景 | `hevc-main10-hdr10-4k60-30s.mkv` | GTX 1060 / 32.0.15.8180；Windows HDR 关闭；窗口默认尺寸；调试层 Auto | 60 | 3 s + 30 s | `test-app-recovery.ps1 -Mode performance` |
| S1-D | 同 S1，调试层关闭 | R2(b) 对照 | 同上 | 同上，`-D3D11DebugLayer Off` | 60 | 同上 | 同上 |
| S1-S | 同 S1，有采样 | R2(b) 对照；重现 2026-09-11 测法 | 同上 | 同上，调试层 Auto | 60 | 全片 | `-Mode playback` |
| S1-R | 同 S1，Release 应用 | R2(c) 判定前提 | 同上 | Release 自包含包或 `bin\x64\Release`；无探针 | 60 | 手动播放 ≥ 30 s | 读取会话日志中每 300 帧的"本段"统计 |
| S2 | 4K60 SDR 8-bit，应用内 | 分离色调映射开销 | `hevc-main-sdr-4k60-30s.mkv` | 同 S1 | 60 | 3 s + 30 s | `-Mode performance` |
| S3 | 4K60 HDR10 → SDR，离屏，Debug，vsync 1 | 与应用内对照，量化窗口/合成器影响 | `hevc-main10-hdr10-4k60-30s.mkv` | 表面 3840×2160、BGRA8、调试层 Auto | 60 | 3 s + 20 s | `PlaybackPerformanceTests` |
| S3-D | 同 S3，调试层关闭 | R2(b) 对照 | 同上 | `MPVSHELL_D3D11_DEBUG_LAYER=0` | 60 | 同上 | 同上 |
| S3-V0 | 同 S3-D，vsync 0 | 原始吞吐上限 | 同上 | `MPVSHELL_TEST_PERF_SYNC_INTERVAL=0` | 无（只记录） | 同上 | 同上 |
| S3-R | 同 S3，Release 测试构建 | R2(c) 判定前提 | 同上 | `-c Release` | 60 | 同上 | 同上 |
| S4 | 4K30 HEVC HDR10，离屏，Debug，有采样 vs 无采样 | PERF-02 复核 | `hevc-main10-hdr10-4k30.mkv` | 表面 3840×2160、BGRA8 | 30 | 2 s + 5 s；`MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS=0.1` 与 `0` 各一次 | `PlaybackPerformanceTests` |
| S5 | 4K30 AV1 软件回退，离屏 | PERF-02 复核 | `av1-main10-sdr-4k30.mkv` | 同 S4，`MPVSHELL_TEST_PERF_REQUIRE_HWDEC=0` | 30 | 同 S4 | 同上 |
| S6 | 4K60 HDR10 PQ 输出，应用内 | 与 RTX 3070 PQ 记录对齐，仅当显示器支持且 Windows HDR 开启 | `hevc-main10-hdr10-4k60-30s.mkv` | 需 HDR 显示器 | 60 | 3 s + 30 s | `-Mode performance` |
| S7 | LG 演示片，应用内，无采样 | PERF-01 原始输入补充 | 私人文件 | 同 S1 | 59.94 | 3 s + 30 s | `-Mode performance -MediaPath <LG.ts>` |

PERF-02 的解读规则：S4/S5 的功能报告丢帧（2026-09-12 记录 105/110）来自 `HardwareDecodeIntegrationTests` 的驱动方式——前 60 帧后在渲染线程反复 `Render` + staging 读回，并在 Debug 调试层下运行。若 S4/S5 的"无采样"窗口满足 R1 而"有采样"窗口不满足，则 105/110 归入 R2(b)，并在功能测试中保持"不设性能断言"的现状；否则进入 R2(a)/(c) 流程。

## 5. 执行记录

> 以下均为待执行；执行后按场景编号填入实测，附报告路径。失败与未达标记录必须保留。

| 场景 | 日期 | 构建 / 调试层 / 采样 | 呈现 fps | VO 丢帧 | 解码丢帧 | Render 均值 / p95 / 最大 (ms) | Present 均值 / p95 (ms) | 判定 | 报告 |
|---|---|---|---|---|---|---|---|---|---|
| S1 | 待执行 | Debug / Auto / 无 | — | — | — | — | — | 待验证 | — |
| S1-D | 待执行 | Debug / Off / 无 | — | — | — | — | — | 待验证 | — |
| S1-S | 待执行 | Debug / Auto / 有 | — | — | — | — | — | 待验证 | — |
| S1-R | 待执行 | Release / 无 / 无 | — | — | — | — | — | 待验证 | — |
| S2 | 待执行 | Debug / Auto / 无 | — | — | — | — | — | 待验证 | — |
| S3 | 待执行 | Debug / Auto / 无 | — | — | — | — | — | 待验证 | — |
| S3-D | 待执行 | Debug / Off / 无 | — | — | — | — | — | 待验证 | — |
| S3-V0 | 待执行 | Debug / Off / 无 / vsync 0 | — | — | — | — | — | 仅记录 | — |
| S3-R | 待执行 | Release / 无 / 无 | — | — | — | — | — | 待验证 | — |
| S4 | 待执行 | Debug / Auto / 有 + 无 | — | — | — | — | — | 待验证 | — |
| S5 | 待执行 | Debug / Auto / 有 + 无 | — | — | — | — | — | 待验证 | — |
| S6 | 待执行 | 需 HDR 显示器 | — | — | — | — | — | 待验证 | — |
| S7 | 待执行 | Debug / Auto / 无 | — | — | — | — | — | 待验证 | — |

## 6. Windows 侧执行步骤

仓库根目录，PowerShell 7。先生成/校验素材（4K60 编码需要数分钟）：

```powershell
pwsh -File build/testing/prepare-hdr-4k-media.ps1
pwsh -File build/testing/prepare-ts-seek-media.ps1
pwsh -File build/testing/prepare-perf-media.ps1
```

构建与常规回归（新增测试不依赖素材时也会运行链路自检）：

```powershell
dotnet build mpv-winui.slnx -c Debug -p:Platform=x64 -v:minimal
dotnet build mpv-winui.slnx -c Release -p:Platform=x64 -v:minimal
dotnet test mpv-winui.slnx -c Debug -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/p1-01/test-results/Debug
```

应用内场景 S1 / S1-D / S1-S / S2（每条命令一个独立进程，报告在 `artifacts/app-recovery/<运行标识>/`）：

```powershell
$hdr = (Resolve-Path artifacts/hdr-4k/media/hevc-main10-hdr10-4k60-30s.mkv).Path
$sdr = (Resolve-Path artifacts/hdr-4k/media/hevc-main-sdr-4k60-30s.mkv).Path
pwsh -File build/testing/test-app-recovery.ps1 -Mode performance -MediaPath $hdr -ReportDirectory artifacts/p1-01/app/S1
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode performance -MediaPath $hdr -D3D11DebugLayer Off -ReportDirectory artifacts/p1-01/app/S1-D
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode playback -MediaPath $hdr -ReportDirectory artifacts/p1-01/app/S1-S
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode performance -MediaPath $sdr -ReportDirectory artifacts/p1-01/app/S2
```

离屏场景 S3 系列（同一 PowerShell 内依次改变一个变量）：

```powershell
$env:MPVSHELL_TEST_PERF_MEDIA = $hdr
$env:MPVSHELL_TEST_HARDWARE_REPORT_DIR = Join-Path (Get-Location) artifacts/p1-01/offscreen
$env:MPVSHELL_TEST_PERF_TARGET_FPS = '60'
$env:MPVSHELL_TEST_PERF_LABEL = 'S3-debug-layer-auto'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
$env:MPVSHELL_D3D11_DEBUG_LAYER = '0'; $env:MPVSHELL_TEST_PERF_LABEL = 'S3-D-debug-layer-off'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
$env:MPVSHELL_TEST_PERF_SYNC_INTERVAL = '0'; $env:MPVSHELL_TEST_PERF_LABEL = 'S3-V0-vsync0'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
Remove-Item Env:MPVSHELL_TEST_PERF_SYNC_INTERVAL, Env:MPVSHELL_D3D11_DEBUG_LAYER
$env:MPVSHELL_TEST_PERF_LABEL = 'S3-R-release'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Release -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
```

PERF-02 场景 S4 / S5：

```powershell
$env:MPVSHELL_TEST_PERF_MEDIA = (Resolve-Path artifacts/hdr-4k/media/hevc-main10-hdr10-4k30.mkv).Path
$env:MPVSHELL_TEST_PERF_TARGET_FPS = '30'; $env:MPVSHELL_TEST_PERF_WARMUP_SECONDS = '2'; $env:MPVSHELL_TEST_PERF_SECONDS = '5'
$env:MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS = '0.1'; $env:MPVSHELL_TEST_PERF_LABEL = 'S4-hevc-4k30-sampled'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
$env:MPVSHELL_TEST_PERF_READBACK_INTERVAL_SECONDS = '0'; $env:MPVSHELL_TEST_PERF_LABEL = 'S4-hevc-4k30-unsampled'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
$env:MPVSHELL_TEST_PERF_MEDIA = (Resolve-Path artifacts/hdr-4k/media/av1-main10-sdr-4k30.mkv).Path
$env:MPVSHELL_TEST_PERF_REQUIRE_HWDEC = '0'; $env:MPVSHELL_TEST_PERF_LABEL = 'S5-av1-4k30-unsampled'
dotnet test tests/MpvShell.Rendering.WinUI.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~PlaybackPerformanceTests.Natural_playback"
```

Release 应用（S1-R）：用 `bin\x64\Release` 或当前发布包播放 `$hdr` 至少 30 秒后关闭，从 `%LOCALAPPDATA%\MpvShell\logs` 最新会话日志中摘录"已呈现 … 本段 …"各行；每行的 fps 与分布只描述该 300 帧段。

## 7. 待办与限制

- 数据未回填前，PERF-01 与 PERF-02 保持"待验证"；不得从工具就绪推断问题已关闭。
- 离屏 Composition SwapChain 未绑定视觉，`Present` 是否受显示器 vblank 节流以报告中 Present 分布为准；应用内数据是 PERF-01 关闭的主证据。
- `performance` 模式属于 Debug 探针；Release 只能依赖日志段统计，没有 JSON 报告。
- 若 S1 系列在关闭调试层后满足 R1，还需 S1-R 确认 Release 表现，才能把 2026-09-11 的 14 fps 归入 R2(b)。
