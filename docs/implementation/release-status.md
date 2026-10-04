# 当前发布包入口

> 更新：2026-10-04。本文件是当前可用包的唯一登记入口。
> 当前为 GTX 1060 Release 自包含包；构建、常规回归、原生发布校验、最终目录运行检查和用户 HDR→SDR 窗口/全屏复测均已记录。P1-01 / PERF-01 已关闭，Phase 1 尚未完成。

## 1. 当前登记的包

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-gtx1060-20261002-015850-fd48b9` |
| ZIP | [下载本机测试包](../../artifacts/gtx1060/MpvShell-gtx1060-20261002-015850-fd48b9.zip)，547,315,551 字节，约 547 MB |
| 解压目录 | `artifacts/gtx1060/MpvShell-gtx1060-20261002-015850-fd48b9/` |
| 配置 | Release / win-x64，.NET 与 WinUI 自包含，PDB、许可证、两份 4K60 / 30 秒 HEVC SDR/HDR10 素材 |
| 构建来源 | `d9dc4d4e9f651f05d420392bf002d61a2fab797a`，SDK 10.0.401；产品/测试代码未修改，装配时文档修改已登记为 `sourceDirty=true` |
| ZIP SHA-256 | `982657138616204895D8B8F0271B988F5F0B43866DAEA06CD53071B586803F5D` |
| libmpv SHA-256 | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| 清单 | 包内 `build-info.json`、`source-hashes.json`、`publish-verification.json`、`package-content-sha256.json`；ZIP 旁附 `.sha256` |
| 本轮构建/回归 | Release 构建 0 警告/错误；常规全量测试 199 通过、0 失败、5 项需素材或性能输入的测试跳过。测试环境调整与失败历史见发布证据 |
| 发布验证 | 四原生 DLL 哈希/x64/加载、Client API 2.5、3 次会话创建销毁通过；ZIP 592 个文件全部核验 |
| 最终目录运行 | GTX 1060 5GB / 驱动 32.0.15.8266，Windows HDR 报告开启，1104×721 窗口；SDR 与 HDR10 两份素材硬解到 EOF，正常退出码 0，日志收集通过。HDR 样片实际为 PQ 输出，未人工目视 |
| 待验证 | PERF-02 的有采样/无采样独立对照，以及跨屏、长期稳定性、其他硬件等既有延后范围 |
| 证据 | [GTX 1060 发布记录](evidence/P1-01-04-gtx1060-test-package.md)、[用户复测收口](evidence/P1-01-05-gtx1060-user-acceptance.md)、`artifacts/gtx1060-check-20261002/` |

两份性能素材的 SHA-256 与 9 月 22 日 RTX 3070 包一致，便于同输入比较；本包不附旧包中的 HEVC 4K30、AV1、PQ 渐变素材。日志入口名 `Start-Debug` 表示详细日志，实际仍为 Release，未开启 D3D11 调试层。文件名中的显卡型号表示测试对象，不是对其他硬件的支持声明。

## 2. 测试与日志

1. 完整解压，阅读 `GTX1060-测试清单.md`，关闭 Windows HDR。
2. 分别双击 `01-SDR-4K60.cmd` 和 `02-HDR-to-SDR-4K60.cmd`，每个场景独立顺播到 EOF 后关闭。
3. 两个入口分别再做一次全屏顺播；暂停、seek、窗口切换等交互另开一次。记录实际分辨率和窗口模式。
4. 填写 `Test-Notes.txt`，关闭播放器并等待退出码，再双击 `Collect-Logs.cmd`；带回生成的 `MpvShell-GTX1060-Logs-*.zip`。

本机验证日志已单独保存，不在交付 ZIP 内；不要把 `artifacts/gtx1060/` 下本机生成的日志 ZIP 当作明日的新测试结果。

## 3. 生成与登记

通用底层发布仍使用 PowerShell 7 的 `build/testing/publish-mvp.ps1 -OutputDirectory <仓库 artifacts 下的新目录>`。本次装配脚本及步骤见发布证据；脚本绑定本次目录，后续复用需为新包创建独立路径，不能覆盖登记包。

原 `publish-rtx3070.ps1 -IncludeTestMedia` 仍可生成包含三份基础素材的 RTX 命名测试包；它不会自动生成本次两份 4K60 样片或 GTX 测试入口，不能将两种装配流程混同。素材分别由 `prepare-hdr-4k-media.ps1` 和 `prepare-perf-media.ps1` 生成并验证。

只有实际完成打包、源码/原生/ZIP 哈希登记、最终目录播放与正常退出后，才更新本入口。构建通过或源码提交前进，不代表包已更新；未运行的媒体/硬件场景保留未验证，不自动继承其他包结论。

## 4. 历史包与复测

- 9 月 22 日包 `MpvShell-rtx3070-20260922-151754-5ea211`，源码 `1afdb995d5a70c0c43e5d5a1ae0e58daf32ac599`，ZIP SHA-256 `567F98EA79D3C29EC634EC18AE31650625D1EA9F42A1565744D4CAF40ECCBC02`，567,140,726 字节。装配与当时本机证据见 [历史发布记录](evidence/P1-01-02-rtx3070-test-package.md)。本次检出目录没有该旧包二进制。
- 用户提供的 `artifacts/MpvShell-RTX3070-Logs-20260922-214856-eb902692.zip` 已在 10 月 2 日复核：短时 4K 全屏 SDR/PQ 约 60 fps、窗口 HDR→SDR 约 60 fps、正常退出，用户目视无问题；开头丢帧与未验证边界见 [RTX 日志复核](evidence/P1-01-03-rtx3070-log-review.md)。不再将该轮 RTX 3070 测试记为未收到。
- 9 月 12 日 TS 修复包及更早产物的历史身份与清理范围见 [清理记录](artifacts-cleanup-2026-09-22.md) 和 [TS 修复证据](evidence/ts-seek-keyframe-2026-09-12.md)。历史路径表示当时产物，不保证在当前检出中存在。
