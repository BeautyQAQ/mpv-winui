# Phase 0 实施进度

> 总体状态：进行中（P0-00、P0-01 已完成）
> 当前工作包：无（P0-01 已完成；P0-06 可启动，P0-02 等待 EXT-03）
> 最后更新：2026-08-28  
> 架构基线：`docs/architecture.md` v1.1  
> 执行计划：`docs/implementation/phase-0-plan.md`

## 1. 状态说明

| 状态 | 含义 |
|---|---|
| 未开始 | 尚未实施 |
| 进行中 | 当前正在实施，尚未满足全部完成标准 |
| 阻塞 | 缺少外部输入、权限、硬件或架构决策，无法安全继续 |
| 待人工验证 | 自动化部分完成，但硬件或视觉验收尚未完成 |
| 通过 | 工作包的代码、自动化和要求的人工验证全部通过 |
| 失败 | 已有证据表明硬闸口不满足，需要重新评估 |

只有存在可复核证据时才能标记“通过”。代码合并、能够编译或能够看到画面均不自动等于通过。

## 2. 当前基线

审计日期：2026-08-28（P0-00 当日复核）。

- 基线提交：`add45b6e9c9634f1b3617013e14ee9e887819c26`（分支 `main`，2026-08-28）
- 解决方案：`mpv-winui.slnx`
- SDK：.NET SDK `10.0.400`（MSBuild 18.9.6，运行时 Microsoft.NETCore.App 10.0.11 / Microsoft.WindowsDesktop.App 10.0.11）
- 开发机（x64 开发机确认）：Windows 11 专业版 10.0.26200，x64，主 GPU NVIDIA GeForce GTX 1060 5GB（驱动 32.0.15.8180，2025-10-29）
- HDR 验证机：未确认（见 EXT-07）
- 目标生产项目：App、Abstractions、LibMpv、Rendering.WinUI 共 4 个；过渡期 Sidecar、VideoHost 继续保留
- 目标测试项目：对应目标生产项目共 4 个；过渡期旧项目测试继续保留
- 默认配置构建：通过，0 警告、0 错误（2026-08-28 P0-01 复核确认）
- 默认配置测试：通过 33 个（2026-08-28 P0-01 复核确认）
  - `MpvShell.Player.Abstractions.Tests`：2
  - `MpvShell.Player.LibMpv.Tests`：2
  - `MpvShell.Rendering.WinUI.Tests`：7
  - `MpvShell.Player.MpvSidecar.Tests`：5
  - `MpvShell.Interop.VideoHost.Tests`：1
  - `MpvShell.App.Tests`：16
- x64 显式配置：通过；解决方案与项目均固定 `Platform=x64`、`PlatformTarget=x64`
- 原生运行时资产：尚未加入仓库
- libmpv 控制、Render API、ANGLE/D3D11、4K 和 HDR：均未实施、未验证

基线命令：

```powershell
dotnet build mpv-winui.slnx --no-restore
dotnet test mpv-winui.slnx --no-build --no-restore
```

P0-01 前的已知失败命令（历史记录，现已修复）：

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
```

历史失败摘要：解决方案配置 `Debug|x64` 无效；P0-01 已增加 x64 配置并验证通过。

## 3. 工作包状态

| 工作包 | 状态 | 负责人/Agent | 开始 | 完成 | 提交 | 备注 |
|---|---|---|---|---|---|---|
| P0-00 基线与输入确认 | 通过 | Copilot Agent | 2026-08-28 | 2026-08-28 | b2f4ccf |
| P0-01 目标项目骨架和抽象边界 | 通过 | Codex | 2026-08-28 | 2026-08-28 | `21070a4` | 目标项目、x64 配置和无 HWND 抽象均已验证 |
| P0-02 原生依赖与确定性加载 | 未开始 |  |  |  |  | 依赖 mpv/ANGLE 来源确认 |
| P0-03 libmpv C ABI 互操作层 | 未开始 |  |  |  |  | 依赖固定头文件 |
| P0-04 会话生命周期 | 未开始 |  |  |  |  |  |
| P0-05 命令、事件和播放控制 | 未开始 |  |  |  |  | Gate A |
| P0-06 D3D11 与 SwapChainPanel 基线 | 未开始 |  |  |  |  |  |
| P0-07 ANGLE/EGL 与 OpenGL FBO | 未开始 |  |  |  |  | 依赖 ANGLE 来源确认 |
| P0-08 Render API SDR 集成 | 未开始 |  |  |  |  | Gate B |
| P0-09 覆盖层、输入与生命周期 | 未开始 |  |  |  |  | Gate C |
| P0-10 4K、硬解和 HDR 验证 | 未开始 |  |  |  |  | Gate D，依赖真实硬件 |
| P0-11 切换、清理与 Phase 0 验收 | 未开始 |  |  |  |  | 仅 Gate A～D 全部通过后开始 |

## 4. 闸口状态

| 闸口 | 状态 | 自动化证据 | 人工证据 | 结论 |
|---|---|---|---|---|
| Gate A：libmpv 控制 | 未开始 | 无 | 无 | 未验证 |
| Gate B：SDR Render API | 未开始 | 无 | 无 | 未验证 |
| Gate C：XAML 覆盖与输入 | 未开始 | 无 | 无 | 未验证 |
| Gate D：4K、硬件解码与 HDR | 未开始 | 无 | 无 | 未验证 |

## 5. 外部输入与阻塞项

| 编号 | 输入/阻塞项 | 状态 | 需要时间 | 负责人 | 证据或决定 |
|---|---|---|---|---|---|
| EXT-01 | mpv v0.41.0 x64 libmpv 构建来源、构建参数和许可证 | 已确认：来源策略 | P0-02 前 | Agent（执行与审计） | 使用 mpv 官方签名 tag `v0.41.0` 自建 win-x64 共享 libmpv；Meson 参数以 `-Dlibmpv=true -Ddefault_library=shared -Dcplayer=false -Dgpl=false -Dbuild-date=false` 为起点。P0-02 必须锁定完整 commit、FFmpeg/libplacebo/libass 等全部依赖版本，审计 LGPL 兼容闭包后才可落盘 |
| EXT-02 | 与二进制匹配的 `client.h`、`render.h`、`render_gl.h` | 已确认 | P0-03 前 | Agent（执行） | 直接取最终 libmpv 构建所使用的同一 `v0.41.0` commit 源码树，并记录文件 SHA-256 |
| EXT-03 | ANGLE x64 固定版本、来源、构建参数和许可证 | 阻塞：待精确版本审计 | P0-02/P0-07 前 | Agent | 来源策略确认为 ANGLE 官方源码自建 x64 Release；P0-02 前仍须给出完整 commit、DEPS/CIPD 闭包和 GN 参数，显式启用 D3D11、避免其他后端静默回退，并完成最小 EGL/D3D11 冒烟验证 |
| EXT-04 | 可再生成或许可证清晰的 SDR 测试媒体 | 已确认：生成策略 | P0-05 前 | Agent（生成） | 使用固定版本 ffmpeg 生成合成 SDR 测试素材；生成脚本、参数、工具版本和素材哈希入库，不依赖公网素材，不把 ffmpeg 作为应用运行时依赖 |
| EXT-05 | 本地 HTTP/HLS 测试服务和固定媒体 | 已确认：实现策略 | P0-05 前 | Agent（实现） | 使用仓库内可重复启动的 .NET 本地静态 HTTP 服务和 ffmpeg 生成的 HLS 分段，不依赖公网或开发机全局服务 |
| EXT-06 | 4K HEVC Main10、AV1、HDR10 和 10-bit 渐变素材 | 阻塞：依赖 EXT-07 硬件就绪 | P0-10 前 | 用户 | 素材候选来源待硬件确认后一并确定 |
| EXT-07 | HDR 显示器、GPU、驱动和 Windows 测试环境 | 部分确认 | P0-10 前 | 用户 | 开发机已确认：Windows 11 10.0.26200、GTX 1060 5GB、驱动 32.0.15.8180；**HDR 显示器状态未确认**，P0-10 前必须给出 HDR 验证机清单 |

说明：EXT-01、EXT-02、EXT-04、EXT-05 的来源/实现策略已确认，具体构建产物仍须由对应工作包固定版本、审计许可证并登记哈希。EXT-03 在精确 commit 和构建闭包确定前继续阻塞 P0-02/P0-07。仓库中当前不存在任何来源不明的 DLL 或 runtimes 资产（2026-08-28 全仓扫描确认为空）。

当前没有任何 Phase 0 技术闸口被判定为通过。

## 6. 验证记录

每次验证追加记录，不覆盖失败历史。

| 日期 | 工作包 | 环境 | 命令/操作 | 结果 | 证据路径 | 备注 |
|---|---|---|---|---|---|---|
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet build mpv-winui.slnx --no-restore` | 通过：0 警告、0 错误 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 默认配置 |
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet test mpv-winui.slnx --no-build --no-restore` | 通过：21/21 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 默认配置 |
| 2026-08-28 | 规划基线 | .NET SDK 10.0.400 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` | 失败：`Debug|x64` 配置无效 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | P0-01 修复 |
| 2026-08-28 | P0-00 | Windows 11 10.0.26200 x64，GTX 1060 5GB（驱动 32.0.15.8180） | `git log -1` / `dotnet --info` | 通过：基线提交 add45b6，SDK 10.0.400，主机架构 x64 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 环境固化 |
| 2026-08-28 | P0-00 | 同上 | `dotnet build mpv-winui.slnx --no-restore` | 通过：0 警告、0 错误 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 基线复核 |
| 2026-08-28 | P0-00 | 同上 | `dotnet test mpv-winui.slnx --no-build --no-restore` | 通过：21/21（Abstractions 1、MpvSidecar 3、VideoHost 1、App 16） | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 基线复核 |
| 2026-08-28 | P0-00 | 同上 | 全仓扫描 `runtimes/` 目录与 `*.dll/lib/a` 文件（排除 bin/obj） | 通过：仓库中不存在任何原生二进制资产 | `docs/implementation/evidence/P0-00-01-baseline-verification.md` | 确认无来源不明 DLL |
| 2026-08-28 | P0-01 | .NET SDK 10.0.400，x64 | `dotnet sln mpv-winui.slnx list` | 通过：目标 4 个生产项目与 4 个目标测试项目均存在；旧项目按计划暂留 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 项目骨架 |
| 2026-08-28 | P0-01 | 同上 | `dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore` | 通过：12 个项目，0 警告、0 错误 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 显式 x64 |
| 2026-08-28 | P0-01 | 同上 | 新增 Abstractions 架构守卫后的首次 x64 构建 | 失败：测试谓词触发 `CS8122`；改为 LINQ 过滤后重试通过 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 保留失败历史 |
| 2026-08-28 | P0-01 | 同上 | `dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore` | 通过：33/33 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 显式 x64 |
| 2026-08-28 | P0-01 | 同上 | 默认配置 build/test | 通过：0 警告、0 错误；33/33 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 默认配置解析为 x64 |
| 2026-08-28 | P0-01 | 同上 | 边界扫描 `hostHandle|HWND|MpvSidecar|VideoHost` | 通过：Abstractions、LibMpv、Rendering.WinUI 无匹配 | `docs/implementation/evidence/P0-01-01-project-boundaries.md` | 无旧边界泄漏 |

## 7. 技术决策记录

本表只记录 Phase 0 实施中由实测决定的事项；已经在 `docs/architecture.md` 锁定的路线不在此重新讨论。

| 编号 | 日期 | 决策 | 状态 | 依据 | 影响 |
|---|---|---|---|---|---|
| DEC-01 |  | HDR 使用 PQ 10-bit 或 scRGB 16-bit float | 待实测 | P0-10 | SwapChain 格式、ColorSpace、FBO 精度 |
| DEC-02 |  | 默认硬件解码配置 | 待实测 | P0-10 | 性能、兼容性和 GPU 资源路径 |
| DEC-03 |  | 是否增加极薄 C++/WinRT 图形桥接 | 待实测 | P0-06/P0-07 | 项目结构和原生资源所有权 |
| DEC-04 |  | 最低 Windows/驱动支持范围 | 待实测 | P0-09/P0-10 | 发布要求和已知限制 |

## 8. 风险与异常记录

| 编号 | 日期 | 工作包 | 风险/异常 | 影响 | 处理状态 | 结论 |
|---|---|---|---|---|---|---|
| RISK-01 | 2026-08-28 | P0-01 | `.slnx` 缺少有效 `Debug|x64` 配置 | 文档规定的 x64 命令不可用 | 已解决 | P0-01 增加 x64 平台；实际属性、构建和测试均验证通过 |

## 9. 人工验证记录格式

所有要求人工验证的工作包（含各 Gate）按以下格式在 `docs/implementation/evidence/` 下建立记录文件（命名：`<工作包>-<序号>-<简述>.md`），并在下方索引表登记。截图/视频/日志原始文件随记录存放于同目录。

每条记录必填字段：

| 字段 | 说明 |
|---|---|
| 日期 / 时间 | 执行验证的本地时间 |
| 工作包 / Gate | 对应工作包编号与闸口 |
| 环境 | 机器标识、Windows 版本、GPU 型号、驱动版本、显示器与 HDR 设置、DPI |
| 操作 | 逐步操作步骤（可复核） |
| 预期 | 来自完成标准的预期结果 |
| 实际 | 实际观察到的结果 |
| 日志 | 应用日志/性能数据文件路径 |
| 截图/视频 | 证据文件路径（截图不能单独作为 HDR 色彩结论） |
| 结论 | 通过 / 失败 / 未验证（失败必须附复现条件） |

记录索引：

| 记录文件 | 工作包 | 日期 | 结论 |
|---|---|---|---|
| `P0-00-01-baseline-verification.md` | P0-00 | 2026-08-28 | 通过 |
| `P0-01-01-project-boundaries.md` | P0-01 | 2026-08-28 | 通过 |

## 10. 更新规则

每个工作包开始时：

- 将“当前工作包”改为对应编号。
- 将工作包状态改为“进行中”，记录开始日期和执行者。
- 重新检查外部输入与前置工作包。

每个工作包结束时：

- 追加验证命令和实际结果，包括失败记录。
- 填写提交 SHA、完成日期、证据路径和未验证项。
- 只有全部完成标准满足时才标记“通过”。
- 需要真实硬件但尚未测试时标记“待人工验证”，不能标记“通过”。
- 发生阻塞时写明最小缺失输入、已经尝试的安全检查和恢复条件。
- 产生 Phase 0 实测决策时更新技术决策表；不要静默改变架构。

Phase 0 结束时：

- 逐项核对 `docs/architecture.md` 第 15 节。
- Gate A～D 全部通过后，才把总体状态改为“通过”。
- 任一硬闸口失败时，总体状态必须为“失败”或“阻塞”，并暂停 Phase 1 产品功能开发。
