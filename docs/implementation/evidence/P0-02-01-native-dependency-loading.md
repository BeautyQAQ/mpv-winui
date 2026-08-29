# P0-02 原生依赖清单与确定性加载

**状态**：`进行中` — 源码和加载机制已完成；实际二进制闭包因本机缺少原生工具链尚未生成
**日期**：2026-08-29
**执行**：Codex

## 已完成

- 锁定 mpv `v0.41.0` commit `41f6a645068483470267271e1d09966ca3b9f413`，确认 Client API 2.5，并记录三个 ABI 头文件 SHA-256。
- 锁定 ANGLE Chrome 152 稳定线 `chromium/7977` commit `736ed80c7552a4b267bd54a282b971aa4555cb3e`，记录 DEPS 与 LICENSE SHA-256。
- 固定 ANGLE D3D11-only GN 参数，关闭 D3D9、桌面 GL、Vulkan、Null 和 SwiftShader。
- 增加固定发布布局 `runtimes/win-x64/native/` 的清单复制规则。
- 通过模块初始化器为 libmpv 互操作程序集注册 `NativeLibrary.SetDllImportResolver`，并实现固定绝对路径、PE AMD64、SHA-256、依赖分组和 Client API 版本检查。
- 错误分类覆盖：清单缺失/无效、资产缺失、架构错误、哈希错误、加载失败、导出缺失和 API 不兼容。
- 增加 ANGLE、mpv 构建入口与清单哈希回填脚本；未登记 DLL 会使清单收尾失败。

## 自动化结果

```powershell
dotnet test tests/MpvShell.Player.LibMpv.Tests/MpvShell.Player.LibMpv.Tests.csproj -p:Platform=x64 --no-restore
# 通过 11/11，0 警告、0 错误

dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
# 构建 0 警告、0 错误；全量测试通过 53/53

# PowerShell Parser 检查 build/native/*.ps1
# build-angle.ps1：PASS
# build-mpv.ps1：PASS
# finalize-native-manifest.ps1：PASS
```

新增测试实际覆盖：正确 x64 PE、缺失文件、x86 PE、错误哈希、占位哈希、Client API 主版本错误、最低次版本不足及 2.5 正常路径。

## 尚未满足的完成标准

- `runtimes/win-x64/native/` 尚无真实 DLL，因此没有真实加载烟雾测试。
- FFmpeg、libplacebo、libass 等 mpv 实际链接依赖尚未构建、枚举和逐项完成许可证/哈希登记。
- ANGLE DEPS/CIPD 尚未执行 `gclient sync` 和 `gclient revinfo`，实际随附闭包尚未归档。
- 尚未运行 EGL 初始化并证明实际后端为 D3D11。

## 本机阻塞证据

2026-08-29 工具扫描结果：

- 可用：Git、.NET SDK 10.0.400。
- 缺失：Visual Studio C++ Build Tools、Windows 原生 C++ SDK 工具链、Python、depot_tools、GN、autoninja/Ninja、Meson、clang-cl/cl。
- 磁盘可用空间约 323 GB，不是当前阻塞项。

恢复条件：安装 VS Build Tools C++ x64 + Windows SDK，准备 depot_tools/GN/Ninja/Meson 后执行 `build/native/` 中的固定构建入口；完成闭包扫描、许可证审计、哈希回填和真实加载烟雾测试后，P0-02 才能标记为通过。
