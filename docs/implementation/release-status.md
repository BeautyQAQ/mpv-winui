# 当前发布包入口

> 更新：2026-09-12；由 P1-00 建立。
> 本文件是当前可用包的唯一登记入口；README 和进度表只引用此处。
> 当前为 Phase 0 验收后的测试包，不代表 Phase 1 或 V1 已验收。产物保留在本机 `artifacts/`，不随 Git 分发；新检出仓库需按下方命令生成新包。

## 1. 当前登记的包

| 项目 | 当前值 |
|---|---|
| 包名 | `MpvShell-rtx3070-20260912-133732-eec5d7` |
| [ZIP](../../artifacts/rtx3070/MpvShell-rtx3070-20260912-133732-eec5d7.zip) | `artifacts/rtx3070/MpvShell-rtx3070-20260912-133732-eec5d7.zip` |
| 解压目录 | `artifacts/rtx3070/MpvShell-rtx3070-20260912-133732-eec5d7/` |
| 配置 | Release、win-x64、.NET 与 WinUI 自包含，包含 PDB 和三份合成测试媒体 |
| 构建来源 | `a56cffd16d7d9d00939f993c300045eeab9a8242`，构建时工作区干净，SDK 10.0.401 |
| 核心修复 | 包含 `mpv-demux-seek-skip-to-keyframe.patch`；精确跳转保留一秒回退窗口 |
| ZIP SHA-256 | `333C195B8E6557105E49F98D5909D0A4F3B1B23E8724DED1B097B583CE623D28` |
| libmpv SHA-256 | `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705` |
| 清单 | 包内 `build-info.json`、`source-hashes.json`、`publish-verification.json` |
| 本次登记复核 | ZIP、EXE、应用/后端 DLL、源码清单及四个原生 DLL 哈希一致；四个原生 DLL 与当前依赖锁匹配，见 [P1-00 证据](evidence/P1-00-01-baseline-and-release-entry.md) |
| 既有运行证据 | 打包时 DLL 加载及三次会话烟雾通过；RTX 3070 第四轮 24 次 TS 跳转 0 参考帧错误、0 呈现丢帧、落点误差 ≤12 ms，见 [TS 复测](evidence/ts-seek-keyframe-2026-09-12.md) |

包名中的 `rtx3070` 是外机测试流程的历史命名，不是对所有其他显卡的兼容承诺。已验证硬件范围及性能待复核项以 [Phase 1 进度](phase-1-progress.md) 为准。

## 2. 启动与日志

完整解压后运行包内 `MpvShell.App.exe`。本机仓库根目录可直接执行：

```powershell
& .\artifacts\rtx3070\MpvShell-rtx3070-20260912-133732-eec5d7\MpvShell.App.exe
```

需要详细诊断时双击同目录 `Start-Debug.cmd`。它运行的仍是 Release 应用，通过环境变量开启 debug 文件日志；不是 Debug 构建。关闭应用后填写 `Test-Notes.txt`，再运行 `Collect-Logs.cmd`。应整体复制目录，不能只复制 EXE。

## 3. 生成和更新入口

在仓库根目录使用 PowerShell 7：

```powershell
pwsh -File build/testing/publish-rtx3070.ps1 -IncludeTestMedia
```

不附带样片时省略 `-IncludeTestMedia`；该参数只复制已核验的媒体，不负责生成。每次脚本运行都会创建新的独立目录与 ZIP，不覆盖这里登记的历史包。

新包的登记条件：

1. 记录源码提交、未提交修改、SDK、配置与构建时间；核对包内源码清单、应用 DLL、原生 DLL 和 ZIP 哈希。当前包若有意基于旧提交，记录其适用基线，不能声称等于更新后的源码。
2. 发布脚本返回成功，`publish-verification.json` 确认自包含文件、x64 DLL、依赖锁、加载与三次会话创建/销毁通过。
3. 从新包启动媒体播放并正常关闭，登记日志和实际验证范围；原生、渲染或 HDR 路径发生变化时执行对应媒体/硬件回归。未覆盖场景保留“未验证”，不继承无关机器或模式的结论。
4. 在 `evidence/` 写入结果，更新本表及 [Phase 1 进度](phase-1-progress.md)。包目录保留原名；README 和其他当前状态文档只链接本页。

本次 P1-00 复用既有包与运行证据，只增加文档和登记核验，没有重新打包或新增 GUI 验收。完整的生产发布流程收口属于 P1-05。

## 4. 历史包

| 目录 | 用途与限制 |
|---|---|
| `artifacts/phase-0/win-x64/` | 2026-09-12 12:28 的 P0-11 收尾包；libmpv 为 `932D7071…`，不含随后加入的 TS 修复 |
| `artifacts/hdr-4k/win-x64/` | HDR/4K 早期验收包；具体结果看对应日期证据 |
| `artifacts/mvp/win-x64/` | 2026-09-10 SDR MVP 历史包 |
| `artifacts/rtx3070/` 中其他带时间的包 | 外机各轮复测快照，不因保留在目录中而成为当前版本 |

历史记录里的包路径用于复核当时结果，继续保留；查找当前测试包请始终回到本页。
