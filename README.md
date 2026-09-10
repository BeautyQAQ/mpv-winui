# MpvShell

Windows x64 的 WinUI 3 播放器，使用进程内 libmpv 和 ANGLE/D3D11，把视频直接渲染到 `SwapChainPanel`。

SDR 播放 MVP 已完成，已生成并实测可运行的 win-x64 发布包。支持本地媒体文件、HTTP/HTTPS 直链，提供播放/暂停、进度跳转、音量/静音、轨道、媒体信息和全屏。HDR 与 4K 硬解的硬件验收按 2026-09-10 的任务安排暂缓，不能据此版本推断硬件支持范围。

2026-09-10 已完成 Debug、Release 全量构建，均为 0 警告、0 错误，各通过 95/95 测试。GUI 已实测本地文件选择、正向视频、时间轴、暂停、信息/音轨、音量/静音、全屏及正常关闭；本机 HTTP 视频播放到 EOF，404 错误显示后可从最近列表恢复播放。真实 GPU 测试通过上下色块方向、连续 resize 及同一 mpv core 的渲染上下文重建。

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
.\build\testing\publish-mvp.ps1
& .\artifacts\mvp\win-x64\MpvShell.App.exe
```

发布目录 `artifacts/mvp/win-x64` 包含 .NET 与 WinUI 运行时，应整体复制，不能只复制 EXE。脚本校验四个原生 DLL 的 SHA-256、x64 架构和加载，并执行三次 libmpv 会话创建/销毁，将结果写入目录内的 `publish-verification.json`。已从最终发布目录启动 GUI，并验证本地视频正向播放与暂停。

## 验证与进度

- [MVP 实施记录](docs/implementation/mvp-progress.md)
- [Phase 0 进度](docs/implementation/phase-0-progress.md)
- [架构与原始验收要求](docs/architecture.md)
- [本地 HTTP 测试服务](build/testing/README.md)

应用诊断日志位于 `%LOCALAPPDATA%\MpvShell\logs`。原生媒体日志对网络地址脱敏；测试素材不随项目提交。

当前默认使用软件解码；HDR、4K 硬解、完整 DPI/多显示器矩阵尚未验收。HLS 尚未单独实测，“最近打开”仅保存在本次进程内。完整 Phase 0 仍未验收，详见实施记录。
