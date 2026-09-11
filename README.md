# MpvShell

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

SDR 播放 MVP 已完成，支持本地媒体文件、HTTP/HTTPS 直链，提供播放/暂停、进度跳转、音量/静音、轨道、媒体信息和全屏。2026-09-11 已恢复 HDR/4K 硬解实施：根据片源与 Windows HDR 状态切换 PQ 10-bit 输出或 SDR 色调映射，并显示实际解码路径。HDR 显示器的视觉效果与跨屏行为仍待人工验收，详见 [HDR/4K 记录](docs/implementation/hdr-4k-progress.md)。

2026-09-10 已完成 Debug、Release 全量构建，均为 0 警告、0 错误，各通过 95/95 测试。GUI 已实测本地文件选择、正向视频、时间轴、暂停、信息/音轨、音量/静音、全屏及正常关闭；本机 HTTP 视频播放到 EOF，404 错误显示后可从最近列表恢复播放。真实 GPU 测试通过上下色块方向、连续 resize 及同一 mpv core 的渲染上下文重建。

2026-09-11 本轮 Debug、Release 全量构建均无警告/错误，各通过 138/138 测试。GTX 1060 实测 HEVC Main10 4K 的 D3D11VA 硬解和 AV1 软件回退，真实 GPU 像素测试通过 10-bit 精度、PQ 输出及 SDR 色调映射。显示器 HDR 视觉效果、跨屏与长时间性能仍待验收。

## 构建与运行

需要 Windows x64、.NET 10 SDK 及 Windows App SDK 构建环境。原生 DLL 已固定版本和 SHA-256，随应用输出复制。

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
& .\src\MpvShell.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\MpvShell.App.exe
```

在窗口中点击“打开文件”，或输入完整本地路径、HTTP/HTTPS 媒体直链后点击“打开地址”。也可把一个媒体路径作为应用启动参数。

快捷键：空格播放/暂停，左右方向键跳转 5 秒，F11 或双击画面切换全屏，Esc 退出全屏/关闭浮层。时间轴支持点击、拖动及键盘操作。

## 自包含发布包

在仓库根目录使用 PowerShell 7 一键发布并验证：

```powershell
.\build\testing\publish-mvp.ps1 -OutputDirectory (Join-Path (Get-Location) artifacts/hdr-4k/win-x64)
& .\artifacts\hdr-4k\win-x64\MpvShell.App.exe
```

本轮发布目录 `artifacts/hdr-4k/win-x64` 包含 .NET、WinUI 运行时及第三方许可证，应整体复制，不能只复制 EXE。脚本校验四个原生 DLL 的 SHA-256、x64 架构和加载，并执行三次 libmpv 会话创建/销毁，将结果写入目录内的 `publish-verification.json`。本轮发布与 35 份许可证原文哈希校验已通过。2026-09-10 的 SDR MVP 包保留在 `artifacts/mvp/win-x64`，其 GUI 正向播放与暂停已有历史记录。

## 验证与进度

- [MVP 实施记录](docs/implementation/mvp-progress.md)
- [HDR/4K 实施与晚间测试](docs/implementation/hdr-4k-progress.md)
- [Phase 0 进度](docs/implementation/phase-0-progress.md)
- [架构与原始验收要求](docs/architecture.md)
- [本地 HTTP 测试服务](build/testing/README.md)

应用诊断日志位于 `%LOCALAPPDATA%\MpvShell\logs`。原生媒体日志对网络地址脱敏；测试素材不随项目提交。

当前优先使用 D3D11VA，经 ANGLE 直接导入 GPU 解码帧；不可用时回退软件解码，信息面板报告实际结果。完整 HDR、DPI/多显示器和长时间性能矩阵尚未验收。HLS 尚未单独实测，“最近打开”仅保存在本次进程内。完整 Phase 0 仍未验收，详见实施记录。
