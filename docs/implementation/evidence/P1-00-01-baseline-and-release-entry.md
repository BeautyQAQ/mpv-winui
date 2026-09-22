# P1-00：Phase 1 基线、工作包与发布入口

| 字段 | 记录 |
|---|---|
| 日期 | 2026-09-12，北京时间 |
| 基线 | `main` / `f793d0b`；检查开始时工作区干净；本次仅修改文档和解决方案中的文档索引 |
| 环境 | Windows 11 10.0.26200 x64、PowerShell 7.6.4、.NET SDK 10.0.401、GTX 1060 5GB / 驱动 32.0.15.8180 |
| 范围 | 建立 Phase 1 计划与进度，整理当前/历史状态，核验并统一既有包入口 |
| 产物 | [计划](../phase-1-plan.md)、[进度](../phase-1-progress.md)、[发布入口](../release-status.md)，README 和历史记录交叉引用 |

## 1. 源码基线复核

以下构建与测试在本次规划前的同一会话中实际执行（约 14:14～14:17），不是从旧进度表抄录。原始 TRX 与媒体报告保存在 [本轮报告目录](../../../artifacts/progress-audit-20260912/)。构建成功输出留在任务记录，未另存构建日志。

| 项目 | 结果 | 证据 |
|---|---|---|
| Debug 构建 | 退出码 0；0 警告、0 错误 | 下方构建命令的任务输出 |
| Release 构建 | 退出码 0；0 警告、0 错误 | 同上 |
| Debug 全量，显式提供四份媒体 | 退出码 0；188 通过、0 失败、0 跳过 | `artifacts/progress-audit-20260912/test-results/Debug/*.trx` |
| Release 常规全量 | 退出码 0；184 通过、0 失败、4 跳过 | `artifacts/progress-audit-20260912/test-results/Release/*.trx` |
| 项目数量 | 4 个生产项目、4 个测试项目 | `mpv-winui.slnx` |

构建命令：

```powershell
dotnet build mpv-winui.slnx -c Debug -p:Platform=x64 --no-restore -v:minimal
dotnet build mpv-winui.slnx -c Release -p:Platform=x64 --no-restore -v:minimal
```

Debug 媒体回归命令（仓库根目录，PowerShell 7；路径对应已有固定样本）：

```powershell
$env:MPVSHELL_TEST_HEVC_MEDIA = (Resolve-Path artifacts/hdr-4k/media/hevc-main10-hdr10-4k30.mkv).Path
$env:MPVSHELL_TEST_AV1_MEDIA = (Resolve-Path artifacts/hdr-4k/media/av1-main10-sdr-4k30.mkv).Path
$env:MPVSHELL_TEST_HDR_GRADIENT_MEDIA = (Resolve-Path artifacts/hdr-4k/media/pq-gradient-10bit-4k.mkv).Path
$env:MPVSHELL_TEST_TS_MEDIA = (Resolve-Path artifacts/hdr-4k/media/hevc-opengop-seek-720p60.ts).Path
$env:MPVSHELL_TEST_TS_SEEK_TARGETS = '2,4,7,11,16,21,25,9'
$env:MPVSHELL_TEST_HARDWARE_REPORT_DIR = Join-Path (Get-Location) artifacts/progress-audit-20260912/hardware-reports/Debug
dotnet test mpv-winui.slnx -c Debug -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/progress-audit-20260912/test-results/Debug
```

Release 在另一独立 PowerShell 进程中运行，未设置上述媒体变量，因此四项素材驱动测试明确跳过：

```powershell
dotnet test mpv-winui.slnx -c Release -p:Platform=x64 --no-build --no-restore --logger trx --results-directory artifacts/progress-audit-20260912/test-results/Release
```

四项跳过为 HEVC、AV1、PQ 渐变和 TS 跳转；不能把本轮 Debug 的实际执行写成 Release 也执行了四项。TRX 的跳过数按各 `UnitTestResult outcome="NotExecuted"` 统计，不能只读汇总中的 `notExecuted` 属性（该适配器将其写为 0）。

## 2. 媒体测试结论与限制

TS 使用 30 秒合成样片，8 个落点，每个变体先顺播 20 秒。报告：`artifacts/progress-audit-20260912/hardware-reports/Debug/ts-seek-decode-path-comparison.json`。

| 变体 | 顺播参考帧错误 | 跳转参考帧错误 | 落点最大绝对误差 |
|---|---:|---:|---:|
| 硬解、关键帧起读开启、offset=1 | 0 | 0 | 21.33 ms |
| 软解、关键帧起读开启、offset=1 | 0 | 0 | 21.33 ms |
| 硬解、关键帧起读关闭、offset=1 | 0 | 192 | 21.33 ms |
| 硬解、关键帧起读开启、offset=0 | 0 | 0 | 1021.33 ms |

该组目标与此前 LG 真实 TS 的 17 个目标不同，不能混用错误总数或误差。产品配置保留 offset=1；offset=0 变体用于说明缺少回退窗口会丢失目标之前的关键帧。

HEVC 报告确认 D3D11VA / d3d11-egl / P010 的 GPU 帧传递和非黑画面；AV1 在 GTX 1060 上软件回退；PQ 梯度像素测试通过。它们验证功能通路，不是性能验收。本轮 HEVC / AV1 报告分别记录 105 / 110 次呈现丢帧、61 / 62 次呈现，运行约 6 秒；测试没有对这些性能数值设通过断言。应由 P1-01 分开测试自然播放和采样开销，不能以“188 通过”宣布性能问题关闭。

另有 [2026-09-11 的性能记录](buffering-and-app-recovery-2026-09-11.md) 报告 GTX 1060、4K60 HDR→SDR 自然播放约 14 fps。它缺少当前版本同场景关闭证据，交 P1-01 复核；不是本次重新确认的产品缺陷。RTX 3070 PQ 59.94 fps 是不同 GPU/输出模式，保留为该场景证据。

## 3. 发布入口核验

当前包的名称、完整路径、提交与哈希只在 [发布入口](../release-status.md) 登记，避免多个文档分别维护“最新目录”。本次沿用既有 13:37 包；构建来源为 `a56cffd`，后续至 `f793d0b` 的提交只整理证据文档。

2026-09-12 14:21 使用 `Get-FileHash -Algorithm SHA256` 实读 ZIP、EXE、应用/后端 DLL、源码清单和四个原生 DLL，与包内清单、ZIP 校验文件及仓库依赖锁比对；不依赖文件名推断版本。登记前还比对包内源码清单所列 180 个 `src/`、`tests/`、`build/native/`、`build/testing/`、构建属性和解决方案文件，均与当时工作区相符。此后本工作包仅改文档及解决方案文档索引。

结果存于 `artifacts/progress-audit-20260912/phase1-entry-verification.json`：

- ZIP 与 `.zip.sha256` 一致。
- EXE、应用 DLL、后端 DLL、`source-hashes.json` 均与包内相应清单一致。
- 四个原生 DLL 同时匹配 `publish-verification.json` 和当前 `native-dependencies.lock.json`；libmpv 包含 TS 补丁。
- 旧 `artifacts/phase-0/win-x64/` 中 libmpv 实际哈希为 `932D7071…`，与当前 `5E9D2D0D…` 不同，已明确标记为历史包。

包的 DLL 加载、三次会话烟雾和外机运行来自已有 `publish-verification.json` 及 [RTX 3070 第四轮记录](ts-seek-keyframe-2026-09-12.md)。本次没有重新执行发布烟雾、打包或 GUI 验收；登记核验不扩大原包的通过范围。

## 4. 本工作包完成核对

- Phase 1 六个工作包分别定义范围、依赖、产物、验收标准与验证方式；只有 P1-00 在本次执行，P1-01 为下一工作包。
- 停止、倍速、最近打开持久化和完整交互留在后续产品阶段；未擅自加入播放列表或 HLS 自适应功能。
- 跨屏、长期耐久、触屏和其他 DPI 等保留为未验收范围，按既有决定延后，不重新作为 Phase 1 启动阻塞。
- 当前状态页清理过时 HLS/HDR 待验收表述，历史日期下的失败、测试计数和产物路径继续保留。
- 文档检查通过：13 份新增/修改文本均为严格 UTF-8、无 BOM、无控制字符或替换字符，85 个本地 Markdown 链接目标均存在；`git -c core.safecrlf=false diff --check` 退出码 0。
- `dotnet sln mpv-winui.slnx list` 退出码 0，仍为 4 个生产项目和 4 个测试项目。仅增加文档索引，无项目结构或产品代码变更，未因文档编辑重复构建测试。

**P1-00 结论：通过。** 本工作包的文档及解决方案文档索引修改已于 2026-09-12 提交至 `8a4829c`；本次验收所依据的实现与测试输入基线为 `f793d0b`。

P1-00 的通过只说明计划、基线和入口建立，不代表后续工作包已完成，也不替代现有性能、生命周期或硬件验收缺口。
