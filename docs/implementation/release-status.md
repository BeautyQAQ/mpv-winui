# 候选测试包与历史发布入口

> 更新：2026-10-28。本文件区分当前待验证候选与历史已通过包。
> Phase 3 候选已封包并核验内容，但显式回归原生崩溃、GUI 未验证，P3-00 待验证；历史通过包仍为 Phase 1。PERF-02 与既有额外硬件/长期验证独立保留。

## 1. 当前候选：待验证

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-phase3-candidate-20261028-121903-72991b` |
| 候选目录 | `artifacts/candidates/MpvShell-phase3-candidate-20261028-121903-72991b/` |
| 主 ZIP | [下载候选测试包（待验证）](../../artifacts/candidates/MpvShell-phase3-candidate-20261028-121903-72991b.zip)，134,886,266 字节，约 135 MB |
| 主 ZIP SHA-256 | `1767214D187DB69C42E95076793C0B99965029BA31EC0E11A9778BA17DAC6E89` |
| 配置与功能 | Release / `win-x64`，.NET 与 WinUI 自包含；包含 Phase 2 停止/倍速、持久化最近记录、时间轴及界面改进 |
| 来源 | `b9e4d2b560f8a96f6b206bfb6fbe418ced8d29af` 加本轮工具/文档，`sourceDirty=true`；产品源码未改；SDK 10.0.403 |
| 源码快照 | [288 文件源码 ZIP](../../artifacts/candidates/MpvShell-phase3-candidate-20261028-121903-72991b-source.zip)，逐项核验通过，SHA-256 `834235CE62C1D6AB19664EB8242EB4FA9A5141F27D3BF8C28C08651B720FD66A` |
| 原生与发布烟雾 | 四 DLL 与锁定清单及 AMD64 一致，实际加载成功、Client API 2.5；临时/最终目录各 3 次会话创建销毁通过 |
| 内容核验 | 主 ZIP 694 条目、源码 288 文件、35 份原生许可来源、26 个 NuGet 包、15 份素材全部核验；188 项构建输入与基线及当前工作区一致 |
| 包内状态说明 | `baseline-summary.json`、`baseline-evidence-sha256.json`、`候选验证清单.md` 明确未通过完整验收，包旁附 `.verification.json` 和 `.sha256` |
| 本轮默认回归 | Debug/Release 构建均 0 警告/错误，各 525 通过、0 失败、9 跳过 |
| 本轮显式回归 | 两配置各记录 529 项通过后在原生渲染调用发生 `0xC0000005`，命令退出 1，整轮失败 |
| 最终目录 GUI | 进程及窗口创建；工具无法绑定窗口，未验证实际画面/操作。只强制清理本次测试进程，未完成正常退出与重启验证；用户历史已恢复 |
| 日志 | `MpvShell-phase3-candidate-Logs-20261028-122552-18c04814.zip`；本机运行日志已移出候选目录，不进入交付 ZIP |
| 当前结论 | 待验证候选，不替代历史已通过包；原生崩溃/独立性能挂起诊断与最终目录 GUI/正常退出仍需完成 |
| 证据 | [P3-00 基线与候选记录](evidence/P3-00-01-baseline-and-candidate.md)、`artifacts/p3-00/` |

候选工具使用 `publish-candidate.ps1` 与 `seal-candidate-package.ps1`，默认输出 `artifacts/candidates/`，包名及日志前缀与历史 Phase 1 区分。封包报告只能证明文件内容和来源一致，不能关闭显式回归失败或替代 GUI 验收。包内源码快照保留构建当时状态，后补结果文档不改变该身份。

## 2. 最近通过的历史包：Phase 1

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-phase1-20261018-120914-200896` |
| ZIP | 历史路径 `artifacts/phase-1/MpvShell-phase1-20261018-120914-200896.zip`，134,822,757 字节，约 135 MB；本次检出未找到该文件 |
| 解压目录 | `artifacts/phase-1/MpvShell-phase1-20261018-120914-200896/` |
| 配置 | Release / win-x64，.NET 10.0.13 与 WinUI 自包含，PDB、原生/托管许可证、三份 4K 样片及 P1-04 小样本 |
| 构建来源 | `d7522cba3e06c75ead25c91f700d10d9d5717ee5` + 构建时尚未提交的 P1-04 修复/测试和 P1-05 脚本，`sourceDirty=true`；SDK 10.0.402。实际输入由快照和哈希确定，不能仅检出基线提交替代 |
| 源码快照 | 历史 257 文件源码 ZIP `MpvShell-phase1-20261018-120914-200896-source.zip`，SHA-256 `70FDABC9B0613E4FE01F6F195BBB124A65B2F9D3919C833AABF4BF4323D66BB5`；本轮未重新核验 |
| ZIP SHA-256 | `BAD4D64F8157F71A12E78651822378F370383475E8220B453FB26ACC68BA90D7` |
| libmpv SHA-256 | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| 清单 | `build-info.json`、`source-hashes.json`、`publish-verification.json`、`package-verification.json`、`package-content-sha256.json`、两类许可证来源清单；ZIP 旁附 `.sha256` |
| 本轮构建/回归 | Debug/Release 构建各 0 警告/错误；默认各 298 通过、9 跳过；显式素材各 307 通过、0 失败、0 跳过 |
| 发布验证 | 四原生 DLL 哈希/x64/加载、Client API 2.5；临时目录及最终目录各 3 次会话创建销毁通过；35 份原生许可、26 个依赖包的许可/元数据，691 个 ZIP 文件及 257 个源码快照文件全部核验 |
| 最终目录运行 | GTX 1060 5GB / 驱动 32.0.15.8266，RDP 会话；多轨与 HEVC HDR10 样片硬解到 EOF、退出码均 0、日志收集成功。Windows HDR 关闭，4K 片源实际输出 SDR tone-map，表面 1658×1084；已保存实际画面截图 |
| 待验证 | PERF-02 独立对照，跨屏、长期稳定性及其他硬件矩阵；本轮 RDP / 4K 离屏测量约 10.67 fps，为“已测量、未判定”，没有性能通过结论 |
| 证据 | [P1-05 发布与阶段验收](evidence/P1-05-01-release-and-phase-1-acceptance.md)、`artifacts/p1-05/` |

`TestMedia/4k/` 提供 HEVC HDR10、AV1 SDR、PQ 渐变；`TestMedia/p1-04/` 提供视频 HLS、多音轨/SRT/ASS、纯音频和纯视频样本。不附较大的 4K60 性能素材；后者的本轮输入哈希和结果单独保存在验收记录。`Start-Debug` 表示详细日志，实际仍为 Release，D3D11 调试层未启用。本包没有重新执行 RTX 3070 等外机矩阵。

## 3. 历史 Phase 1 包的测试与日志

1. 完整解压，阅读 `README-测试说明.md`；直接运行 `MpvShell.App.exe`，或将样片拖到 `Start-Debug.cmd` 记录详细日志。
2. 按用途测试包内样片、暂停/继续、seek、轨道与信息；记录显卡、表面分辨率、窗口/全屏、Windows HDR 以及远程/本机会话。
3. 关闭播放器并等待退出码，填写 `Test-Notes.txt`，运行 `Collect-Logs.cmd` 生成 `MpvShell-Phase1-Logs-*.zip`。
4. 远程自动化时保持桌面解锁且 RDP 窗口展开；锁屏或最小化时可能黑截图/输入拒绝，不据此判断播放器画面。

本机两次运行日志保存在 `artifacts/p1-05/gui-logs/` 与 `artifacts/phase-1/MpvShell-Phase1-Logs-20261018-122013-1d3c0849.zip`，不在交付 ZIP 内；它们不代表后续外机测试结果。

## 4. 生成与登记

新候选使用 PowerShell 7 的 `publish-candidate.ps1 -HdrMediaDirectory <已校验4K目录> -P104MediaDirectory <已校验P1-04目录> -PackageLabel phase3-candidate -PackageDate <登记日期yyyyMMdd>`。记录实际候选目录运行结果，将本机运行日志移到包外，再用 `seal-candidate-package.ps1 -PackageDirectory <目录>` 核对内容并封包。未完成 GUI 或存在回归失败时，入口必须保留“待验证候选”和具体限制，不能仅凭封包成功登记为已通过。

通用底层发布仍使用 PowerShell 7 的 `build/testing/publish-mvp.ps1`。Phase 1 用 `publish-phase1.ps1 -HdrMediaDirectory <已校验4K目录> -P104MediaDirectory <已校验P1-04目录>` 创建独立目录及源码快照；从最终目录完成验收并移出运行日志后，用 `seal-phase1-package.ps1 -PackageDirectory <目录>` 核对并封包，不覆盖已登记 ZIP。详细步骤见 [发布工具说明](../../build/testing/README.md#phase-1-发布与封包)。

原 `publish-rtx3070.ps1` 保留旧装配用途，不会自动生成本轮源码快照/托管许可集合；本轮采用通用候选脚本，Phase 1 入口仅保留兼容。素材由 `prepare-hdr-4k-media.ps1`、`prepare-p1-04-media.ps1` 等入口生成。许可与版本来源已记录，不承诺重新构建的二进制逐字节相同。

只有实际完成构建/回归、源码/原生/ZIP 哈希核验及最终目录播放与正常退出后，才登记为已通过包；待验证候选单列身份、失败和缺口。源码提交前进不代表包已更新；未运行的媒体/硬件场景保留未验证，不自动继承其他包结论。

## 5. 其他历史包与复测

- 10 月 2 日 GTX 1060 包 `MpvShell-gtx1060-20261002-015850-fd48b9`，源码 `d9dc4d4e9f651f05d420392bf002d61a2fab797a` + 当时文档改动，SDK 10.0.401，547,315,551 字节，ZIP SHA-256 `982657138616204895D8B8F0271B988F5F0B43866DAEA06CD53071B586803F5D`；旧包保留于 `artifacts/gtx1060/`。199 通过/5 跳过与用户 HDR→SDR 窗口/全屏复测只属于该历史身份，见 [发布记录](evidence/P1-01-04-gtx1060-test-package.md)和[用户收口](evidence/P1-01-05-gtx1060-user-acceptance.md)。
- 9 月 22 日包 `MpvShell-rtx3070-20260922-151754-5ea211`，源码 `1afdb995d5a70c0c43e5d5a1ae0e58daf32ac599`，ZIP SHA-256 `567F98EA79D3C29EC634EC18AE31650625D1EA9F42A1565744D4CAF40ECCBC02`，567,140,726 字节。装配与当时本机证据见 [历史发布记录](evidence/P1-01-02-rtx3070-test-package.md)。本次检出目录没有该旧包二进制。
- 用户提供的 `artifacts/MpvShell-RTX3070-Logs-20260922-214856-eb902692.zip` 已在 10 月 2 日复核：短时 4K 全屏 SDR/PQ 约 60 fps、窗口 HDR→SDR 约 60 fps、正常退出，用户目视无问题；开头丢帧与未验证边界见 [RTX 日志复核](evidence/P1-01-03-rtx3070-log-review.md)。不再将该轮 RTX 3070 测试记为未收到。
- 9 月 12 日 TS 修复包及更早产物的历史身份与清理范围见 [清理记录](artifacts-cleanup-2026-09-22.md) 和 [TS 修复证据](evidence/ts-seek-keyframe-2026-09-12.md)。历史路径表示当时产物，不保证在当前检出中存在。
