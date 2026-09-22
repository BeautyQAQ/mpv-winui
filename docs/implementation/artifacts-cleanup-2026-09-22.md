# 产物目录清理（2026-09-22）

按用户要求清理 `artifacts` 中已被当前测试包替代的运行文件、重复下载和空发布临时目录。只操作本仓库的 `artifacts/`，未修改产品代码；清理前逐项解析绝对路径，确认没有符号链接/目录联接，并保护当前 RTX 3070 包。

## 结果

| 项目 | 结果 |
|---|---|
| 清理前文件总字节数 | 5,209,285,050（约 5.21 GB） |
| 清理后文件总字节数 | 2,211,360,704（约 2.21 GB） |
| 净释放 | 2,997,924,346 字节（约 3.00 GB / 2.79 GiB） |
| 清理目标 | 22 项，包含 8 份旧版/重复解压包及 4 个旧 ZIP |
| 历史记录存档 | 227 个文件，复制后及清理后均核对 SHA-256 |
| 现有保留文件校验 | 985 个文件清理前后 SHA-256 全部一致 |

容量按文件长度汇总，不包含文件系统分配差异；上述清理后数字是在随后增加本说明和目录索引前测得。

## 清理与保留范围

- 移除 2026-09-11/12 的四份 RTX 3070 发布目录、四个 ZIP 及其旧校验侧文件。包身份、源码/媒体清单、校验记录、说明和许可证等小文件已保存到 [archive/retired-packages](../../artifacts/archive/retired-packages/)，存档内部沿用原 `artifacts/` 相对路径。
- 移除 `mvp/win-x64`、`hdr-4k/win-x64`、`phase-0/win-x64` 的运行文件；原路径重新放回同一份 `publish-verification.json`，附已清理说明，避免历史报告引用失效。
- 移除 `rtx3070-validation/中文 测试包/` 内旧包的重复运行文件；其已填写的 `Test-Notes.txt`、原始 `logs/` 和身份清单已逐文件存档；旁边的日志 ZIP、硬件报告和 TRX 原样保留。
- 移除 `native-hdr-candidate`、`native-rebuild` 的候选/重复 DLL；保留原生重编日志，正式依赖仍位于源码和当前包内。
- 移除已解压的 FFmpeg `.7z`、`.zip` 重复下载；完整的 `ffmpeg-8.1.2-essentials_build/` 工具目录保留，并用当前素材清单核对 FFmpeg 可执行文件哈希。
- 移除空的 `.publish-f542c6b038b4469dba3fc24aa63abb3d` 失败目录及一份没有对应 ZIP 的历史 SHA256 文件（校验记录已存档）。

当前 `MpvShell-rtx3070-20260922-151754-5ea211` 解压目录、ZIP 和 SHA256 保持不变；ZIP 哈希仍为 `567F98EA79D3C29EC634EC18AE31650625D1EA9F42A1565744D4CAF40ECCBC02`。回归素材、原生工具、全部独立测试证据、截图、失败日志和外机原始日志包保留。

## 清理证据

[本机审计目录](../../artifacts/archive/cleanup-20260922/) 包含：`cleanup-plan.json`（精确目标）、`removed-tree-inventory.json`（移除前文件清单及保留标记）、`preserved-records.json`（存档映射/哈希）、`protected-files-before.json`（保留文件快照）、`cleanup-result.json`（执行结果）。其中的 `cleanup.ps1` 固定绑定本次目标，仅作本次操作记录，不是通用定期清理规则。

当前测试包位置以 [发布入口](release-status.md) 为准；`artifacts/README.md` 提供本机目录索引。历史文档保留原验证日期和结果，已清理的旧二进制不能再从旧路径运行。
