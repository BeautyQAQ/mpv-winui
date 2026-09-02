# P0-02 原生依赖清单与确定性加载

**状态**：`通过`

**日期**：2026-08-29 至 2026-08-30

**执行**：Codex

## 验收结论

- 真实构建 mpv v0.41.0 与 ANGLE `chromium/7977` 成功；所有源码、子模块、WrapDB 包、参数和补丁均已锁定。
- 应用运行时闭包固定为四个 x64 DLL：`libmpv-2.dll`、`libEGL.dll`、`libGLESv2.dll`、`d3dcompiler_47.dll`。
- `libmpv-2.dll` 使用 Clang/LLD 23、Release、`/MT` 静态 CRT 和 LGPL-only 配置；FFmpeg、libplacebo、libass、字体栈和 zlib 均静态链接。libpng fallback 已解析但最终链接规则未引用。
- `dumpbin /dependents` 未发现 VC Runtime 或未登记第三方 DLL；四个文件均已完成 SHA-256 登记。
- 清单驱动的固定绝对路径加载通过，`mpv_client_api_version()` 实测为 2.5。
- ANGLE EGL 初始化通过，实际渲染器为 NVIDIA GTX 1060 的 D3D11 后端，EGL 1.5 / OpenGL ES 3.0。
- Debug x64 构建输出和 Release `win-x64` 自包含发布目录都包含与清单一致的完整闭包；两个目录再次执行 mpv 与 ANGLE 烟雾测试均通过。

## 构建环境

- Windows 11 x64，NVIDIA GeForce GTX 1060 5GB，驱动 32.0.15.8180。
- Visual Studio Community 2026 18.9.2，MSVC 14.51.36231，Windows SDK 10.0.26100.0。
- Clang/LLD 23.0.0git，LLVM commit `53d18800eda3b7407e53366f27ca78e922c6e0db`。
- Meson 1.9.2；Ninja 来自锁定 ANGLE 工具闭包。
- 依赖获取使用当前进程代理 `127.0.0.1:7890`，没有修改系统代理或在应用运行时下载依赖。

## 真实二进制

| 文件 | 大小（字节） | SHA-256 |
|---|---:|---|
| `libmpv-2.dll` | 27,606,016 | `D0D01C2AF708423E6B281ADC58A39FAFD1FB2CFA8FE76D437D76383F088BA228` |
| `d3dcompiler_47.dll` | 4,741,568 | `E407B9FFADEF47E87994DC488214F75405CF04EE99589F2315FC7AB99FD1DFE2` |
| `libGLESv2.dll` | 4,957,184 | `55ECF05D47B4CCFCF91B4F5FE0F21ED3FCE2584B6CC54E4F6855A65142F32D3C` |
| `libEGL.dll` | 203,776 | `8B912D254D5FC8755840B6FD1E404F36E466521FD63F68255528CD7F101909D7` |

详细版本、许可证、源码哈希、静态链接闭包和系统 DLL 导入列表见 `docs/implementation/native-dependency-manifest.md` 与 `build/native/source-lock.json`。

## 自动化与烟雾测试

```text
finalize-native-manifest.ps1（对临时清单副本）
PASS：全部清单资产存在，SHA-256 一致，无未登记 DLL

test-native-closure.ps1（源码资产目录与应用输出目录）
LOAD PASS  d3dcompiler_47.dll
LOAD PASS  libGLESv2.dll
LOAD PASS  libmpv-2.dll
LOAD PASS  libEGL.dll
MPV API PASS  2.5

test-angle.ps1（源码资产目录与应用输出目录）
EGL 1.5
ANGLE 2.1.1 git hash 736ed80c7552
ANGLE (NVIDIA, NVIDIA GeForce GTX 1060 5GB, Direct3D11)
OpenGL ES 3.0

dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
PASS：0 警告，0 错误

dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
PASS：53/53

dotnet publish src/MpvShell.App/MpvShell.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:Platform=x64
PASS：发布成功；发布目录四 DLL + 清单逐文件一致；两个烟雾测试通过
```

加载器的 11 个自动化测试继续覆盖正确 x64 PE、缺失文件、x86 PE、错误哈希、占位哈希、Client API 主版本错误、最低次版本不足和 2.5 正常路径。测试使用临时文件与固定路径，不依赖开发机全局 mpv 或 `PATH`。

## 构建中发现并固化的问题

1. libplacebo 在关闭 Vulkan 后端时仍需要 Vulkan 公共类型；补齐其自身锁定的 `Vulkan-Headers` 子模块，仅作编译期头文件。
2. FFmpeg Meson 端口在 clang-cl 下错误寻找 GNU 风格符号工具；仓库补丁改用 Visual Studio `dumpbin`。
3. libass 0.17.4 向 `_BitScanReverse` 传入了错误指针类型；仓库补丁将结果变量改为 Windows ABI 要求的 `unsigned long`。
4. mpv v0.41.0 向 MSVC `rc.exe` 传入 GNU 风格 codepage 参数；仓库补丁改为 `/c65001`。

这些补丁均由 `build-mpv.ps1` 幂等检查并应用，源文件位于 `build/native/patches/`。

## 非目标确认

本工作包只做最小 DLL 加载、版本调用和 EGL 初始化；没有创建 mpv 会话、播放媒体或调用 libmpv Render API。上述内容由 P0-03 及后续工作包继续实现。
