# P0-02 原生依赖构建入口

本目录锁定源码与构建参数，不保存第三方源码缓存，也不从 `PATH` 或开发机全局目录复制运行时 DLL。

## 固定版本

- mpv `v0.41.0`：`41f6a645068483470267271e1d09966ca3b9f413`
- ANGLE Chrome 152 稳定线 `chromium/7977`：`736ed80c7552a4b267bd54a282b971aa4555cb3e`

精确头文件、DEPS 和许可证哈希见 `source-lock.json`。

## 构建顺序

1. 安装 Visual Studio Build Tools C++ x64 工具链、Windows SDK、depot_tools、Meson 和 Ninja。
2. 运行 `build-angle.ps1`；脚本只启用 D3D11，关闭 D3D9、桌面 GL、Vulkan、Null 和 SwiftShader 后端。
3. 准备经过许可证审计并固定版本的 mpv 依赖前缀，再运行 `build-mpv.ps1`。
4. 将全部非系统运行时 DLL 登记到 `native-dependencies.lock.json`；不得只登记顶层三个 DLL。
5. 运行 `finalize-native-manifest.ps1` 回填 SHA-256。脚本发现未登记 DLL 时会失败。
6. 把同一闭包复制到应用发布布局的 `runtimes/win-x64/native/`，执行最小加载和 ANGLE D3D11 后端烟雾测试。

当前开发机尚未安装 C++ Build Tools、depot_tools、GN/Ninja/Meson，因此源版本已锁定，实际二进制闭包仍未生成，P0-02 不能标记为通过。
