# P1-01：GTX 1060 独立测试包（2026-10-02）

按用户要求，在完成 [RTX 3070 日志复核](P1-01-03-rtx3070-log-review.md) 后生成供 GTX 1060 后续测试的 Release 自包含包。唯一包入口、ZIP 大小和哈希见 [发布状态](../release-status.md)。本次没有修改产品或测试源码，源码提交为 `d9dc4d4e9f651f05d420392bf002d61a2fab797a`；工作区的文档变更如实登记为 `sourceDirty=true`。

## 构建与回归

证据目录：`artifacts/gtx1060-check-20261002/`。

- .NET SDK 10.0.401，`dotnet build mpv-winui.slnx -c Release -p:Platform=x64`：0 警告、0 错误，见 `build-release.log`。
- 最终 Release 全量常规回归：**199 通过、0 失败、5 跳过，共 204 项**，见 `test-results-final/`、`test-release-final.log`、`test-summary.json`。跳过的是需要显式素材/参数的 HEVC、AV1、PQ 渐变、TS 对照和自然播放吞吐场景；不是这五项已经通过。本次未重新构建 Debug。
- 首次 VSTest 因父进程访问被拒绝而中止；独立隐藏 PowerShell 重试仍失败。根据上游 VSTest 17.14.1 的 `DefaultEngineInvoker`，在测试输出的 runtimeconfig 临时设 `MSTest.EnableParentProcessQuery=false` 后，发现系统 Temp 写入被拒绝。最终仅为本次测试把 `TEMP/TMP` 指向证据目录下的 `temp/` 后通过。测试 runtimeconfig 在 `finally` 中恢复；没有修改全局环境、系统权限、产品配置或仓库测试代码。首次中止及临时目录失败日志均保留。
- `publish-mvp.ps1 -OutputDirectory <独立包目录>` 通过，四个原生 DLL 的锁定哈希、x64、加载、Client API 2.5 和 3 次会话创建/初始化/销毁均通过，见 `publish.log` 与包内 `publish-verification.json`。

## 素材与装配

- 下载仓库登记的 FFmpeg 8.1.2 essentials，工具包 SHA-256 与固定值 `E25B682664025D49034C981AFB4BAE36238A40F29A3CC1C713AD9A8B5B3528F6` 一致。首次遇到证书吊销服务器离线，采用 curl 的 best-effort 吊销检查并继续校验 TLS 证书；下载中断后断点续传，最后核验完整工具哈希。工具仅用于生成素材，不附入播放器包。
- 用原 `prepare-perf-media.ps1` 生成两份 3840×2160 / 60 fps / 30 秒 HEVC 素材，FFprobe 核验编码、位深、色彩标记、时长和 HDR10 首帧静态元数据。
- HDR 样片 SHA-256：`36B23343E601DBCF111836A7758A6343E18A845C239BA6FC8DFF1B846E887428`；SDR 样片：`914CA4BAFEFF712AAAECFE71AB8C10D8BF40C0616D7BE47F2E0C64D52AB6DA57`。两者与 9 月 22 日 RTX 3070 包逐字节一致。
- 本包只附这两份性能素材，未附旧包的 AV1、PQ 渐变及 HEVC 4K30 素材。保留运行时、许可证、PDB、源码哈希、构建/发布清单。
- 沿用便携脚本，将包内 RTX3070 标签换成 GTX1060；PowerShell 文件保留便携输出所需的 UTF-8 BOM，以支持 Windows PowerShell 5.1。README 按两份素材调整，增加 `01-SDR-4K60.cmd`、`02-HDR-to-SDR-4K60.cmd`、`GTX1060-测试清单.md` 和逐场景测试记录。
- 一次性装配、素材验证、启动检查和最终压缩校验脚本分别保存为证据目录的 `assemble-package.ps1`、`verify-media.ps1`、`smoke-package.ps1`、`finalize-package.ps1`。这些脚本绑定本次包路径，是可复核的装配记录，不是通用发布入口。

## 最终目录运行检查

通过系统 Windows PowerShell 5.1 的包内 `Start-Debug.ps1`，分别启动两份素材，检查 D3D11VA、实际呈现、完整 30 秒 EOF，再请求正常关闭。两次应用退出码均为 0，渲染资源与会话有释放记录，`Collect-Logs.ps1` 成功收集日志；见 `gui-smoke-verification.json`、`gui-smoke-retry.log`、`gui-smoke-logs/`。

首次自动检查因读取正在写入的日志时共享模式冲突而中断，应用仍正常播到 EOF；随后正常关闭并修正检查脚本的共享读取方式，再完成两次独立检查。该失败属于检查脚本，不是播放器崩溃；原始记录保留。

本机实际为 GTX 1060 5GB，驱动 `32.0.15.8266`，1104×721 窗口，Windows 报告 HDR 开启。SDR 样片输出 BGRA8，HDR 样片实际输出 10-bit PQ（目标 486 nit）。因此本次启动检查**没有覆盖 HDR 关闭后的 HDR→SDR**，也没有人工目视确认、全屏或调试层/采样对照。没有修改系统 HDR 设置，不把自动运行检查视为用户明日性能验收。

最终压缩排除本机试运行日志。`package-content-sha256.json` 登记交付文件，`final-package-verification.json` 核对 ZIP 每个条目的大小与解压内容哈希；独立 `.sha256` 登记 ZIP 哈希。原始日志移到证据目录保存。

## 交付后的验证范围

先在 Windows HDR 关闭时分别做 SDR、HDR→SDR 的默认窗口/全屏四次独立顺播，交互测试另开一次；填写 `Test-Notes.txt`，关闭播放器后运行 `Collect-Logs.cmd`。场景由实际输出尺寸和窗口模式决定，非 4K 显示器的全屏不能记为 4K 表面。

本包最初作为后续测试输入，生成时尚未关闭 P1-01、PERF-01、PERF-02。2026-10-04 用户确认 GTX 1060 的 HDR→SDR 窗口和全屏此前复测正常，P1-01 / PERF-01 据此关闭；PERF-02 的 Debug 调试层、像素采样对照仍保留为独立跟进项，详见[用户复测收口](P1-01-05-gtx1060-user-acceptance.md)。
