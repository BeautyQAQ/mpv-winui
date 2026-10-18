# 当前发布包入口

> 更新：2026-10-18。本文件是当前可用包的唯一登记入口。
> 当前为 Phase 1 Release 自包含包；P1-00～P1-05 已在声明范围内通过。PERF-02 与既有额外硬件/长期验证独立保留。

## 1. 当前登记的包

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-phase1-20261018-120914-200896` |
| ZIP | [下载本机发布包](../../artifacts/phase-1/MpvShell-phase1-20261018-120914-200896.zip)，134,822,757 字节，约 135 MB |
| 解压目录 | `artifacts/phase-1/MpvShell-phase1-20261018-120914-200896/` |
| 配置 | Release / win-x64，.NET 10.0.13 与 WinUI 自包含，PDB、原生/托管许可证、三份 4K 样片及 P1-04 小样本 |
| 构建来源 | `d7522cba3e06c75ead25c91f700d10d9d5717ee5` + 构建时尚未提交的 P1-04 修复/测试和 P1-05 脚本，`sourceDirty=true`；SDK 10.0.402。实际输入由快照和哈希确定，不能仅检出基线提交替代 |
| 源码快照 | [257 文件源码 ZIP](../../artifacts/phase-1/MpvShell-phase1-20261018-120914-200896-source.zip)，SHA-256 `70FDABC9B0613E4FE01F6F195BBB124A65B2F9D3919C833AABF4BF4323D66BB5` |
| ZIP SHA-256 | `BAD4D64F8157F71A12E78651822378F370383475E8220B453FB26ACC68BA90D7` |
| libmpv SHA-256 | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| 清单 | `build-info.json`、`source-hashes.json`、`publish-verification.json`、`package-verification.json`、`package-content-sha256.json`、两类许可证来源清单；ZIP 旁附 `.sha256` |
| 本轮构建/回归 | Debug/Release 构建各 0 警告/错误；默认各 298 通过、9 跳过；显式素材各 307 通过、0 失败、0 跳过 |
| 发布验证 | 四原生 DLL 哈希/x64/加载、Client API 2.5；临时目录及最终目录各 3 次会话创建销毁通过；35 份原生许可、26 个依赖包的许可/元数据，691 个 ZIP 文件及 257 个源码快照文件全部核验 |
| 最终目录运行 | GTX 1060 5GB / 驱动 32.0.15.8266，RDP 会话；多轨与 HEVC HDR10 样片硬解到 EOF、退出码均 0、日志收集成功。Windows HDR 关闭，4K 片源实际输出 SDR tone-map，表面 1658×1084；已保存实际画面截图 |
| 待验证 | PERF-02 独立对照，跨屏、长期稳定性及其他硬件矩阵；本轮 RDP / 4K 离屏测量约 10.67 fps，为“已测量、未判定”，没有性能通过结论 |
| 证据 | [P1-05 发布与阶段验收](evidence/P1-05-01-release-and-phase-1-acceptance.md)、`artifacts/p1-05/` |

`TestMedia/4k/` 提供 HEVC HDR10、AV1 SDR、PQ 渐变；`TestMedia/p1-04/` 提供视频 HLS、多音轨/SRT/ASS、纯音频和纯视频样本。不附较大的 4K60 性能素材；后者的本轮输入哈希和结果单独保存在验收记录。`Start-Debug` 表示详细日志，实际仍为 Release，D3D11 调试层未启用。本包没有重新执行 RTX 3070 等外机矩阵。

## 2. 测试与日志

1. 完整解压，阅读 `README-测试说明.md`；直接运行 `MpvShell.App.exe`，或将样片拖到 `Start-Debug.cmd` 记录详细日志。
2. 按用途测试包内样片、暂停/继续、seek、轨道与信息；记录显卡、表面分辨率、窗口/全屏、Windows HDR 以及远程/本机会话。
3. 关闭播放器并等待退出码，填写 `Test-Notes.txt`，运行 `Collect-Logs.cmd` 生成 `MpvShell-Phase1-Logs-*.zip`。
4. 远程自动化时保持桌面解锁且 RDP 窗口展开；锁屏或最小化时可能黑截图/输入拒绝，不据此判断播放器画面。

本机两次运行日志保存在 `artifacts/p1-05/gui-logs/` 与 `artifacts/phase-1/MpvShell-Phase1-Logs-20261018-122013-1d3c0849.zip`，不在交付 ZIP 内；它们不代表后续外机测试结果。

## 3. 生成与登记

通用底层发布仍使用 PowerShell 7 的 `build/testing/publish-mvp.ps1`。Phase 1 用 `publish-phase1.ps1 -HdrMediaDirectory <已校验4K目录> -P104MediaDirectory <已校验P1-04目录>` 创建独立目录及源码快照；从最终目录完成验收并移出运行日志后，用 `seal-phase1-package.ps1 -PackageDirectory <目录>` 核对并封包，不覆盖已登记 ZIP。详细步骤见 [发布工具说明](../../build/testing/README.md#phase-1-发布与封包)。

原 `publish-rtx3070.ps1` 保留旧装配用途，不会自动生成本轮源码快照/托管许可集合；需要本轮验收结构时使用 Phase 1 脚本。素材由 `prepare-hdr-4k-media.ps1`、`prepare-p1-04-media.ps1` 等入口生成。许可与版本来源已记录，不承诺重新构建的二进制逐字节相同。

只有实际完成打包、源码/原生/ZIP 哈希登记、最终目录播放与正常退出后，才更新本入口。构建通过或源码提交前进，不代表包已更新；未运行的媒体/硬件场景保留未验证，不自动继承其他包结论。

## 4. 历史包与复测

- 10 月 2 日 GTX 1060 包 `MpvShell-gtx1060-20261002-015850-fd48b9`，源码 `d9dc4d4e9f651f05d420392bf002d61a2fab797a` + 当时文档改动，SDK 10.0.401，547,315,551 字节，ZIP SHA-256 `982657138616204895D8B8F0271B988F5F0B43866DAEA06CD53071B586803F5D`；旧包保留于 `artifacts/gtx1060/`。199 通过/5 跳过与用户 HDR→SDR 窗口/全屏复测只属于该历史身份，见 [发布记录](evidence/P1-01-04-gtx1060-test-package.md)和[用户收口](evidence/P1-01-05-gtx1060-user-acceptance.md)。
- 9 月 22 日包 `MpvShell-rtx3070-20260922-151754-5ea211`，源码 `1afdb995d5a70c0c43e5d5a1ae0e58daf32ac599`，ZIP SHA-256 `567F98EA79D3C29EC634EC18AE31650625D1EA9F42A1565744D4CAF40ECCBC02`，567,140,726 字节。装配与当时本机证据见 [历史发布记录](evidence/P1-01-02-rtx3070-test-package.md)。本次检出目录没有该旧包二进制。
- 用户提供的 `artifacts/MpvShell-RTX3070-Logs-20260922-214856-eb902692.zip` 已在 10 月 2 日复核：短时 4K 全屏 SDR/PQ 约 60 fps、窗口 HDR→SDR 约 60 fps、正常退出，用户目视无问题；开头丢帧与未验证边界见 [RTX 日志复核](evidence/P1-01-03-rtx3070-log-review.md)。不再将该轮 RTX 3070 测试记为未收到。
- 9 月 12 日 TS 修复包及更早产物的历史身份与清理范围见 [清理记录](artifacts-cleanup-2026-09-22.md) 和 [TS 修复证据](evidence/ts-seek-keyframe-2026-09-12.md)。历史路径表示当时产物，不保证在当前检出中存在。
