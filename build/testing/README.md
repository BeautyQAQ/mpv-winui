# 本机 HTTP 播放验证

当前工作包与验收标准见 [Phase 1 计划](../../docs/implementation/phase-1-plan.md)，可用 ZIP、启动方式和发布登记见 [当前发布包入口](../../docs/implementation/release-status.md)。本页说明测试工具，不指定最新产物目录。

`serve-media.py` 仅依赖 Python 标准库，把指定的本地 MP4 文件发布到 `127.0.0.1`，便于使用应用的 URL 输入验证 HTTP 播放和跳转。它只提供 `/sample.mp4`，支持完整 GET、HEAD 和单段字节 Range，不提供目录浏览。

在仓库根目录运行（`python` 应指向已安装的 Python 3）：

```powershell
python build/testing/serve-media.py 'C:\Users\a1426\Downloads\result.mp4'
```

将 `http://127.0.0.1:8765/sample.mp4` 粘贴到播放器中。可通过 `--port 8766` 更换端口，或 `--port 0` 自动选择空闲端口；实际地址会打印到终端。按 Ctrl+C 停止服务。

手动检查响应头和 Range：

```powershell
curl.exe --noproxy '*' --head http://127.0.0.1:8765/sample.mp4
curl.exe --noproxy '*' --range 0-31 --dump-header - --output NUL http://127.0.0.1:8765/sample.mp4
```

完整响应应为 `200`，并带有 `Accept-Ranges: bytes` 和文件总长度；第二条命令应得到 `206`、`Content-Length: 32` 和对应的 `Content-Range`。

播放器验证应至少包含：加载后出现真实视频、播放时间连续增加、暂停/继续、向前及向后跳转、停止并重新加载、播放中关闭窗口。HTTP 服务的响应正确只证明测试输入可用，不能代替播放器运行验证。

## 应用内 GPU 故障恢复验证

`test-app-recovery.ps1` 构建 Debug 应用并通过真实 WinUI 窗口运行故障恢复检查；需要交互式 Windows 桌面和可用 GPU，运行时会显示播放器窗口。故障注入参数仅在 Debug 版本启用。默认生成 64×64、30 fps、20 秒的上红下蓝 Y4M 素材，上方带有横向移动的白色标记，依次验证播放中恢复（`playing`）、暂停时恢复（`paused`）、重建后的首帧再次失败（`immediate-failure`）、重建图形设备本身失败（`rebuild-failure`）、真实窗口尺寸变化中恢复（`resize`），以及 P1-03 新增的生命周期场景：播放/暂停状态下真实页面卸载与重新挂载（`page-rebuild-playing`、`page-rebuild-paused`）、自动重建进行中关闭（`close-during-recovery`）、加载进行中关闭（`close-during-load`，脚本自建只接受连接、不响应请求的本机端口）、恢复租约持有期间换媒体（`recovery-media-switch`，使用自动生成的第二份换色样片）：

```powershell
pwsh -File build/testing/test-app-recovery.ps1
```

应用检查真实帧像素、渲染资源重建和播放状态连续性，并验证后续不可恢复错误和重建失败能到达 `PlayerViewModel`。终态还要求原生媒体暂停、播放入口禁用；音量命令不能清除错误，播放命令和显示器状态变化不能重启媒体/呈现。每个场景使用独立应用进程，结束时释放播放会话、渲染资源及恢复同步锁，关闭窗口并写出 JSON 报告。单个进程限时 90 秒；超时只终止脚本启动的该测试进程。报告必须同时包含 `Status="Passed"` 和布尔值 `ShutdownCompleted=true`；报告缺失或进程非零退出也会使脚本失败。自 P1-03 起，报告还必须确认 `SessionReleased`、`RendererReleased` 与 `ShutdownRepeatSafe`；每个场景的会话日志通过 `MPVSHELL_LOG_DIRECTORY` 重定向到报告目录，脚本核对其中包含“会话已销毁”与“渲染上下文、EGL、SwapChain 和 D3D11 资源已按顺序释放”标记。页面重建场景通过真实 Frame 导航卸载并重新挂载 `PlayerPage`（新页面实例），验证共享播放会话上的媒体、轨道、暂停位置与像素在重建后保持一致；`close-during-recovery` 在自动重建持有生命周期锁与视频租约期间请求关闭；`recovery-media-switch` 在恢复租约持有期间提交换媒体，验证恢复不把旧媒体状态或错误带给新文件。

`close-during-startup-load` 将同类本机挂起 HTTP 地址作为真正的启动媒体参数，观察页面提交的启动加载任务仍未完成时关闭，要求资源释放和加载等待结束共用 5 秒预算。它区别于初始化后另行打开媒体的 `close-during-load`，用于防止页面释放等待启动媒体加载超时。可单独执行：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode close-during-startup-load
```

脚本仅对自行生成的素材传入 `--recovery-test-animated-pattern`，要求播放恢复后相隔至少 0.5 秒的两次像素采样出现变化，以发现时间前进但画面冻结的问题。暂停场景选取第 30 秒或片中点（取较早者），等待原生 seek 结束且位置稳定，再比较恢复前后的实际暂停位置及像素：位置差小于 1ms，归一化 RGB 平均绝对差小于 2/255，相对差小于 2%。自定义视频应在该处有可见内容；不启用运动图案断言。读回请求仅在成功 Present 后完成。

这些场景通过生成故障异常执行真实应用的恢复和错误处理流程，不会触发操作系统 GPU 超时检测与恢复（TDR）或实际显卡驱动重置。

也可使用实际 4K HDR 视频；`-NoBuild` 复用已构建的 Debug 应用，`-Mode` 选择场景：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode paused
```

另有非默认的 `playback` 模式，不注入故障，不强制重绘，间隔读取自然呈现的画面直到 EOF，检查画面变化、硬解模式稳定、正常结束及资源释放。仍受每进程 90 秒上限约束，适用于本次约 75 秒的 LG 样片，不能直接用于任意长片：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode playback -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
```

通过 `-AppPath` 可指定 Debug 可执行文件，`-ReportDirectory` 可指定报告目录。默认报告位于 `artifacts/app-recovery/<时间与唯一标识>/`；每次运行保留独立素材、场景报告和汇总，不覆盖已有文件。使用 HDR 文件通过恢复测试说明该输入的应用播放与恢复流程可用；显示器实际 HDR 输出和颜色准确性仍需另外验证。

## 应用内自然播放性能测量

`performance` 模式（P1-01 新增）在真实窗口内做**无采样**测量：预热 `-WarmupSeconds`（默认 3）后进入 `-SteadySeconds`（默认 30）的稳态窗口，窗口内不做任何 CPU 读回、不强制重绘，只读取渲染线程累计的呈现 fps、Render/Present 耗时分布（均值/中位/p95/最大）和 mpv 的 VO/解码丢帧差值。窗口结束后才读回一次确认画面非黑。报告不对吞吐设通过条件，判定规则见 [P1-01 记录](../../docs/implementation/evidence/P1-01-01-fixed-media-and-performance-scenarios.md)。

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode performance -MediaPath 'artifacts\hdr-4k\media\hevc-main10-hdr10-4k60-30s.mkv'
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode performance -MediaPath '...' -D3D11DebugLayer Off
```

`-D3D11DebugLayer Off` 通过 `MPVSHELL_D3D11_DEBUG_LAYER=0` 让 Debug 应用不创建 D3D11 调试层，用于在同一构建下对照调试层开销；报告中的 `Device.D3D11DebugLayerEnabled` 记录实际状态。`playback` 模式（有间隔采样）现在也输出同样的渲染统计与 mpv 计数，二者对照可量化采样开销。`performance` 模式的进程限时按预热与窗口自动放宽，也可用 `-TimeoutSeconds` 指定。

固定 4K60 性能素材由 `prepare-perf-media.ps1` 生成到 `artifacts/hdr-4k/media/`，清单为 `perf-media-manifest.json`；离屏（不经窗口）的同类测量见 `tests/MpvShell.Rendering.WinUI.Tests/PlaybackPerformanceTests.cs`，通过 `MPVSHELL_TEST_PERF_*` 环境变量配置。

## P1-04 视频 HLS、多音轨与字幕像素验收

使用本机已有的 FFmpeg/FFprobe 生成固定输入（PowerShell 7）。默认工具路径与 `prepare-ts-seek-media.ps1` 相同，也可显式传入 `-FfmpegPath`；脚本不会下载工具。输出只能在仓库 `artifacts/` 下，已有清单默认逐文件核验哈希并复用，显式 `-Regenerate` 才覆盖生成。

```powershell
pwsh -File build/testing/prepare-p1-04-media.ps1
$env:MPVSHELL_TEST_P1_04_MEDIA_DIR = (Resolve-Path artifacts/p1-04/media).Path
$env:MPVSHELL_TEST_P1_04_REPORT_DIR = Join-Path (Get-Location) 'artifacts/p1-04/reports'
dotnet test mpv-winui.slnx -p:Platform=x64 --filter 'FullyQualifiedName~VideoHlsSubtitleIntegrationTests|FullyQualifiedName~BackendMediaInformationTests|FullyQualifiedName~PlayerViewModelTrackSelectionTests' --logger trx --results-directory artifacts/p1-04/tests
```

素材为 320×180 / 24 fps / 12 秒 H.264/AAC VOD（六个两秒 MPEG-TS 分片）、含 AAC/PCM 两音轨及 SRT/ASS 两字幕的 Matroska，以及纯音频 WAV / 无音轨短视频。`media-manifest.json` 保存完整参数、工具哈希、素材哈希与 FFprobe 流信息。字幕是项目自写内容，测试用 Arial 字体来自 Windows，本项目不再分发字体。

四项 `VideoHlsSubtitleIntegrationTests` 使用只绑定 `127.0.0.1` 随机端口的服务，经真实 libmpv Render API、ANGLE/D3D11 Composition SwapChain 检查：变化画面、暂停像素、跨分片定位、EOF 信息、404 后重新加载、受控分片停顿引发的缓冲/恢复、音轨编码与字幕切换/关闭像素、换媒体信息清理及实际调试日志脱敏。测试只在请求验收像素时读回 CPU；生产渲染路径不增加读回。它们无需 WinUI 窗口，不能替代 GUI 人工验收或自然播放性能测量。

未设置 `MPVSHELL_TEST_P1_04_MEDIA_DIR` 时，这四项测试明确跳过；设置路径但素材缺失或哈希不符会失败。`BackendMediaInformationTests` 和 ViewModel 轨道测试不依赖素材，照常执行。JSON 报告默认写在素材目录的同级 `reports/`，每次运行使用独立文件名；是否通过以完整 TRX 结果为准，不能只数 JSON。当前执行结果与覆盖限制见 [P1-04 验收记录](../../docs/implementation/evidence/P1-04-01-video-hls-subtitles-and-media-information.md)。

## Phase 1 发布与封包

先完成 x64 Debug/Release 构建与回归，再用两个已经生成并校验过的素材目录创建独立候选包（PowerShell 7）：

```powershell
pwsh -File build/testing/prepare-hdr-4k-media.ps1 -OutputDirectory artifacts/p1-05/media
pwsh -File build/testing/prepare-p1-04-media.ps1 -OutputDirectory artifacts/p1-04/media-final
pwsh -File build/testing/publish-phase1.ps1 -HdrMediaDirectory artifacts/p1-05/media -P104MediaDirectory artifacts/p1-04/media-final
```

`publish-phase1.ps1` 在 `artifacts/phase-1/` 创建带时间和唯一 ID 的目录及相邻 `-source.zip`。源码快照覆盖 Git 跟踪文件与未忽略的新文件，包括本地未提交修改、四个原生 DLL、原生构建锁、补丁、包装器和许可证；`gitCommit` 是基线，实际源码由 `source-hashes.json` 和快照哈希确定。构建前后核对源码哈希，构建期间不要修改输入文件。快照不包含 `.git`、开发产物或素材工具，重建应用先解压快照，按根目录 README 构建；重建原生依赖使用快照内 `build/native/README.md` 的锁定源和工具。`SourceBuild/` 是随二进制提供的构建说明副本，脚本应在完整源码树内运行。

底层 `publish-mvp.ps1` 仍负责 Release 自包含发布、四 DLL 的 x64/哈希/加载、Client API 和三次会话创建销毁。新包还包含 PDB、三份 4K 样片、P1-04 小样本，以及兼容 Windows PowerShell 5.1 的日志脚本。

`copy-managed-licenses.ps1` 从本次还原的 NuGet 包和自包含运行时包收集许可原文、第三方通知及 nuspec，保存 SHA-256 和包 SHA-512。仅有 MIT 表达式的 SharpGen/Vortice 包使用 `licenses/origins.json` 锁定的上游提交原文，提交号必须与 nuspec 一致；发布时不联网获取许可证。`ThirdParty/nuget-license-origins.json` 覆盖应用还原依赖（含构建工具）与 .NET 运行时，不能用其条目数推断实际发布 DLL 数量。

从新目录运行 `Start-Debug.ps1 -MediaPath <包内样片绝对路径>`，实际检查窗口、画面、EOF 和正常关闭；再运行 `Collect-Logs.ps1`。保留运行证据，将包内 `logs/` 移至独立验收目录，然后封包：

```powershell
pwsh -File build/testing/seal-phase1-package.ps1 -PackageDirectory artifacts/phase-1/MpvShell-phase1-实际包名
```

封包会验证源码 ZIP 的每个文件、原生构建文件与许可证副本、35 项许可证来源哈希、素材及原生闭包；生成 `package-content-sha256.json`，逐条校验成品 ZIP 内容，并写出外部 SHA-256 与核验报告。不覆盖已有 ZIP，不把本机日志带入交付包。该脚本只证明内容一致性，不替代实际 GUI 验收，也不宣称二进制逐字节可重现；全部通过后才更新 [发布入口](../../docs/implementation/release-status.md)。
