# P0-02 原生依赖构建入口

本目录锁定源码、构建参数和必要的上游兼容补丁，不保存第三方源码缓存，也不从 `PATH` 或开发机全局目录复制运行时 DLL。

## 固定版本

- mpv `v0.41.0`：`41f6a645068483470267271e1d09966ca3b9f413`
- ANGLE Chrome 152 稳定线 `chromium/7977`：`736ed80c7552a4b267bd54a282b971aa4555cb3e`
- FFmpeg 8.0.3、libplacebo 7.351.0、libass 0.17.4 及字体依赖的精确 commit/源码哈希见 `source-lock.json`

精确头文件、DEPS 和许可证哈希见 `source-lock.json`。

## 构建顺序

1. 安装 Visual Studio C++ x64 工具链、Windows SDK、Python；ANGLE 同步流程提供 depot_tools、GN、Ninja、Clang 和 Meson Python 包。
2. 运行 `build-angle.ps1`；脚本使用 MSVC 与系统 C++ 标准库，并只启用 D3D11，关闭桌面 GL、Vulkan、Null 和 SwiftShader 后端。
3. 运行 `build-mpv.ps1`；脚本自行检出所有锁定源码和 WrapDB 包，应用仓库中的三个兼容补丁，以 Clang/LLD、`/MT` 和 LGPL 兼容选项构建单一 `libmpv-2.dll`。
4. 将全部非系统运行时 DLL 登记到 `native-dependencies.lock.json`；不得只登记顶层三个 DLL。
5. 运行 `finalize-native-manifest.ps1` 回填 SHA-256。脚本发现未登记 DLL 时会失败。
6. 构建应用后，对输出目录运行 `test-native-closure.ps1` 和 `test-angle.ps1`。

网络访问遵循当前进程的 `HTTP_PROXY`、`HTTPS_PROXY` 和 `ALL_PROXY`；脚本不会修改系统代理。

2026-08-30 已在 Visual Studio Community 2026、Windows SDK 10.0.26100.0、Clang/LLD 23.0.0git 和 Meson 1.9.2 上完成真实构建。应用输出闭包为 `libmpv-2.dll`、`libEGL.dll`、`libGLESv2.dll`、`d3dcompiler_47.dll` 和清单 JSON；两个烟雾测试均通过。
