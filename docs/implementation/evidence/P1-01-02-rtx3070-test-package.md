# P1-01：RTX 3070 外机测试包（2026-09-22）

本次按用户要求生成可直接带到 RTX 3070 设备的独立测试包；当前路径、包身份与 ZIP 哈希统一登记在 [发布入口](../release-status.md)。构建源码为 `1afdb995d5a70c0c43e5d5a1ae0e58daf32ac599`，构建时工作区干净，使用 .NET SDK 10.0.401。发布登记与本证据在完成打包后更新，未修改产品代码。

## 1. 实际执行与结果

证据目录：[artifacts/package-check-20260922-151715](../../../artifacts/package-check-20260922-151715/)。

| 检查 | 结果与证据 |
|---|---|
| 基础样片校验 | `prepare-hdr-4k-media.ps1` 校验三份既有素材；见 `media-check.log` |
| 首次尝试 | `publish-rtx3070.ps1 -IncludeTestMedia -NoRestore` 因缺少 `net10.0-windows10.0.19041.0/win-x64` 还原目标而失败（NETSDK1047）；保留 `publish.log`，未改代码；空临时目录随后按用户要求在[产物清理](../artifacts-cleanup-2026-09-22.md)中移除 |
| 重新发布 | 去掉 `-NoRestore` 后还原并发布成功；`publish-with-restore.log`；Release win-x64 自包含 .NET/WinUI、PDB、许可证与便携启动/收集脚本 |
| 发布目录依赖 | 四 DLL 哈希与锁定清单一致、x64 检查和实际加载通过、Client API 2.5、3 次 mpv 会话创建/初始化/销毁通过；包内 `publish-verification.json` |
| Release 渲染回归 | **68 通过、0 失败、0 跳过**；`test-results/rendering-release.trx`。包含 HEVC/AV1、PQ 渐变、TS 跳转、原生渲染与新增统计/测量自检；显式排除素材驱动的自然播放吞吐测试，此次不判定 4K 性能 |
| 新包启动与播放 | 系统 Windows PowerShell 5.1 执行包内 `Start-Debug.ps1`，传入包内 HEVC Main10 HDR10 4K30 样片；GTX 1060 硬解、D3D11/EGL GPU 帧路径、SDR 表面，播放到 8 秒 EOF |
| 画面与退出 | 自动窗口工具两次因窗口归属识别错误无法截图；用户明确确认“画面正常，已关闭”。启动脚本退出码 0，日志确认渲染资源、会话及主窗口按顺序释放；`gui-smoke-verification.json`、`gui-smoke-logs/`、`package-launch.log` |
| 日志收集 | Windows PowerShell 5.1 执行包内 `Collect-Logs.ps1` 成功收集 1 次记录，退出码 0；`collect-logs-check.log` |
| 最终 ZIP | 五份样片、595 个文件、567,140,726 字节；逐个读取压缩条目并校验内容 SHA-256，全部匹配；`final-package-verification.json`。本机试运行日志未装入交付 ZIP |

渲染回归采用以下输入：`MPVSHELL_TEST_HEVC_MEDIA`、`MPVSHELL_TEST_AV1_MEDIA`、`MPVSHELL_TEST_HDR_GRADIENT_MEDIA` 指向 `artifacts/hdr-4k/media/` 下对应的三份素材；`MPVSHELL_TEST_TS_MEDIA` 指向已有 `hevc-opengop-seek-720p60.ts`，目标为 `2,4,7,11,16,21,25,9` 秒，顺播使用默认 20 秒。原始硬件报告另存 `hardware-reports/`。

```powershell
dotnet test tests/MpvShell.Rendering.WinUI.Tests/MpvShell.Rendering.WinUI.Tests.csproj -c Release -p:Platform=x64 --no-build --no-restore --filter 'FullyQualifiedName!~Natural_playback_throughput_should_be_measured_and_reported_separately_from_functional_checks' --logger 'trx;LogFileName=rendering-release.trx' --results-directory artifacts/package-check-20260922-151715/test-results
```

该命令使用此前同一产品/测试源码构建的 Release 测试输出；发布脚本另行构建并校验最终自包含目录。未重跑整个解决方案全部测试；Debug/Release 构建及各 14 项统计 + 1 项链路自检沿用 [同日记录](P1-01-01-fixed-media-and-performance-scenarios.md#8-构建与测量链路自检2026-09-22)。

## 2. 五份素材与装配

标准 `publish-rtx3070.ps1 -IncludeTestMedia` 已复制 HEVC Main10 HDR10 4K30、AV1 Main10 SDR 4K30、FFV1 PQ 渐变三份基础素材及清单。本次额外执行 `prepare-perf-media.ps1`，追加两份 3840×2160、60 fps、30 秒的合成样片：

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `hevc-main10-hdr10-4k60-30s.mkv` | 217005674 | `36B23343E601DBCF111836A7758A6343E18A845C239BA6FC8DFF1B846E887428` |
| `hevc-main-sdr-4k60-30s.mkv` | 217074255 | `914CA4BAFEFF712AAAECFE71AB8C10D8BF40C0616D7BE47F2E0C64D52AB6DA57` |

FFprobe 核验了编码、帧率、时长、色彩标记及 HDR 首帧静态元数据，并重新运行生成脚本的清单校验分支，两份 SHA-256 一致。包内 `TestMedia/perf-media-manifest.json` 与 `perf-media-verification.json` 保存结果。HDR 样片只用于吞吐/色调映射路径，不能用于真实亮度和色准判断；未运行这两份素材的本轮性能验收。

包内新增 `RTX3070-本次测试清单.md`，说明 SDR、HDR→SDR、具备显示器条件时的 HDR 输出、跳转和窗口操作测试，以及如何对应日志目录填写 `Test-Notes.txt`。现有启动/收集脚本保持原样。

本次装配脚本保存为证据目录下的 `finalize-package.ps1`：校验并复制两份样片与清单，更新包内 `build-info.json` 的样片列表与追加说明，生成 `package-content-sha256.json`，排除本机 `logs/` 后重新压缩，并逐项核对 ZIP 内容与哈希。应用二进制未在追加素材时改变。该脚本绑定本次包名和路径，是本次装配记录；后续包应先用标准发布脚本创建独立目录，再按新身份登记。

## 3. 结论与待验证范围

**本包可交给用户在 RTX 3070 设备测试。** 本机验证范围是远程会话中的 GTX 1060 / SDR 环境：依赖、功能回归、实际发布目录播放、用户目视确认、正常退出和日志收集。

当前未验证本轮 RTX 3070、真实 HDR 显示输出或 4K60 性能；不继承 9 月 12 日旧包的外机结论，也不凭本次功能测试宣布 PERF-01/PERF-02 关闭。Phase 1/P1-01 均仍进行中；等待用户带回本包的日志和操作记录。
