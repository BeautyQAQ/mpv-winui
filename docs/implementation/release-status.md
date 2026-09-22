# 当前发布包入口

> 更新：2026-09-22；由 P1-00 建立，本次登记 P1-01 外机测试包。
> 本文件是当前可用包的唯一登记入口；README 和进度表只引用此处。
> 当前为包含 P1-01 渲染统计的 Release 测试包；本机发布与播放检查通过，本轮 RTX 3070 测试待用户执行，不代表 Phase 1 或 V1 已验收。产物保留在本机 `artifacts/`，不随 Git 分发；新检出仓库需按下方命令生成新包。

## 1. 当前登记的包

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-rtx3070-20260922-151754-5ea211` |
| [ZIP](../../artifacts/rtx3070/MpvShell-rtx3070-20260922-151754-5ea211.zip) | `artifacts/rtx3070/MpvShell-rtx3070-20260922-151754-5ea211.zip`，567,140,726 字节 |
| 解压目录 | `artifacts/rtx3070/MpvShell-rtx3070-20260922-151754-5ea211/` |
| 配置 | Release、win-x64、.NET 与 WinUI 自包含，包含 PDB、五份合成测试媒体与本次 RTX 3070 测试清单 |
| 构建来源 | `1afdb995d5a70c0c43e5d5a1ae0e58daf32ac599`，构建时工作区干净，SDK 10.0.401；登记文档在打包后更新 |
| 主要内容 | 包含 P1-01 呈现帧率与 Render/Present 分布日志；沿用 TS 关键帧起读补丁及一秒回退窗口 |
| ZIP SHA-256 | `567F98EA79D3C29EC634EC18AE31650625D1EA9F42A1565744D4CAF40ECCBC02` |
| libmpv SHA-256 | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| 清单 | 包内 `build-info.json`、`source-hashes.json`、`publish-verification.json`、`package-content-sha256.json` |
| 本次登记复核 | 原生 DLL 哈希/x64/实际加载、Client API 2.5、3 次会话烟雾通过；最终 ZIP 共 595 个文件，清单条目解压后的 SHA-256 全部一致；见 [本次发布证据](evidence/P1-01-02-rtx3070-test-package.md) |
| 本次运行证据 | GTX 1060 / SDR / 远程会话：Release 渲染与媒体回归 68 通过、无失败/跳过；发布目录 HEVC 4K30 硬解播放到 EOF，用户确认画面正常，手动关闭后退出码 0，日志收集成功 |
| 待验证 | 本轮 RTX 3070、真实 HDR 显示输出、4K60 性能与交互测试；旧包的外机结果不自动转移到本包 |

包名中的 `rtx3070` 是外机测试流程的历史命名，不是对所有其他显卡的兼容承诺。已验证硬件范围及性能待复核项以 [Phase 1 进度](phase-1-progress.md) 为准。

## 2. 启动与日志

完整解压后运行包内 `MpvShell.App.exe`。本机仓库根目录可直接执行：

```powershell
& .\artifacts\rtx3070\MpvShell-rtx3070-20260922-151754-5ea211\MpvShell.App.exe
```

需要详细诊断时双击同目录 `Start-Debug.cmd`。它运行的仍是 Release 应用，通过环境变量开启 debug 文件日志；不是 Debug 构建。关闭应用后填写 `Test-Notes.txt`，再运行 `Collect-Logs.cmd`。应整体复制目录，不能只复制 EXE。

本包另附 `RTX3070-本次测试清单.md`，建议先按相同窗口模式分别整片顺播 4K60 SDR 与 HDR→SDR；有 HDR 显示器时再测 Windows HDR 开启后的输出。交互操作另开一次运行，记录日志目录、素材、HDR 状态与窗口模式。五份样片包含三份既有 4K30/PQ 素材及新增的 30 秒 4K60 HDR10/SDR 对照；合成 HDR 测试图不能用于色准判断。

## 3. 生成和更新入口

在仓库根目录使用 PowerShell 7：

```powershell
pwsh -File build/testing/publish-rtx3070.ps1 -IncludeTestMedia
```

不附带样片时省略 `-IncludeTestMedia`；该参数只复制已核验的媒体，不负责生成。每次脚本运行都会创建新的独立目录与 ZIP，不覆盖这里登记的历史包。

上述标准脚本附带三份基础样片。本次测试包另追加了 `prepare-perf-media.ps1` 生成并核验的两份 4K60 样片及包内测试清单，重新生成 ZIP 和哈希；装配步骤、证据与限制见 [本次发布记录](evidence/P1-01-02-rtx3070-test-package.md)。

新包的登记条件：

1. 记录源码提交、未提交修改、SDK、配置与构建时间；核对包内源码清单、应用 DLL、原生 DLL 和 ZIP 哈希。当前包若有意基于旧提交，记录其适用基线，不能声称等于更新后的源码。
2. 发布脚本返回成功，`publish-verification.json` 确认自包含文件、x64 DLL、依赖锁、加载与三次会话创建/销毁通过。
3. 从新包启动媒体播放并正常关闭，登记日志和实际验证范围；原生、渲染或 HDR 路径发生变化时执行对应媒体/硬件回归。未覆盖场景保留“未验证”，不继承无关机器或模式的结论。
4. 在 `evidence/` 写入结果，更新本表及 [Phase 1 进度](phase-1-progress.md)。包目录保留原名；README 和其他当前状态文档只链接本页。

P1-00 当时复用既有包与运行证据；2026-09-22 已实际重新打包并完成上述本机检查。完整的生产发布流程收口仍属于 P1-05。

## 4. 历史包

2026-09-22 按用户要求清理已被替代的运行文件和旧 ZIP。历史构建身份、发布报告、测试备注与日志已保存到 [历史包记录](../../artifacts/archive/retired-packages/)；当前测试包、回归素材/工具与验收证据完整保留。清理范围和校验结果见 [产物清理记录](artifacts-cleanup-2026-09-22.md)。

| 目录 | 用途与限制 |
|---|---|
| 原 `artifacts/rtx3070/MpvShell-rtx3070-20260912-133732-eec5d7/` | 先前登记的 TS 修复包，来源 `a56cffd`；运行文件/ZIP 已清理，身份与媒体清单位于上述存档相同相对路径；其 RTX 3070 第四轮复测仍见 [TS 记录](evidence/ts-seek-keyframe-2026-09-12.md) |
| `artifacts/phase-0/win-x64/` | 2026-09-12 P0-11 历史包；运行文件已清理，原位置保留 `publish-verification.json` |
| `artifacts/hdr-4k/win-x64/` | HDR/4K 早期验收包；运行文件已清理，原位置保留 `publish-verification.json` |
| `artifacts/mvp/win-x64/` | 2026-09-10 SDR MVP 历史包；运行文件已清理，原位置保留 `publish-verification.json` |
| 原 `artifacts/rtx3070/` 中 2026-09-11/12 的其他测试包 | 运行文件/ZIP 已清理，构建与校验记录已存档；`MpvShell-RTX3070-Logs-*.zip` 原始日志包继续保留 |

历史记录里的旧路径描述当时产物，不表示对应二进制仍在本机；复核身份与报告时使用存档。查找当前可运行测试包请始终回到本页。
