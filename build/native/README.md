# P0-02 原生依赖构建入口

本目录锁定源码、构建参数和必要的上游兼容补丁，不保存第三方源码缓存，也不从 `PATH` 或开发机全局目录复制运行时 DLL。

## 固定版本

- mpv `v0.41.0`：`41f6a645068483470267271e1d09966ca3b9f413`
- ANGLE Chrome 152 稳定线 `chromium/7977`：`736ed80c7552a4b267bd54a282b971aa4555cb3e`
- FFmpeg 8.0.3、dav1d 1.5.4、libplacebo 7.351.0、libass 0.17.4 及字体依赖的精确 commit/源码哈希见 `source-lock.json`

精确头文件、DEPS 和许可证哈希见 `source-lock.json`。

## 构建顺序

1. 安装 Visual Studio C++ x64 工具链、Windows SDK、Python；ANGLE 同步流程提供 depot_tools、GN、Ninja、Clang 和 Meson Python 包。
2. 运行 `build-angle.ps1`；脚本使用 MSVC 与系统 C++ 标准库，并只启用 D3D11，关闭桌面 GL、Vulkan、Null 和 SwiftShader 后端。
3. 运行 `build-mpv.ps1`，通过 `-AngleSourcePath` 指向上一步的锁定 ANGLE 源码，通过 `-NasmPath` 指向锁定 NASM 2.16.03；脚本验证源码 commit 与 NASM 哈希。脚本自行检出所有锁定源码和 WrapDB 包，应用仓库中的六个补丁，以 Clang/LLD、`/MT` 和 LGPL 兼容选项构建单一 `libmpv-2.dll`。dav1d 采用静态库，启用 8/10/12 bit 软件 AV1 解码与 x64 SIMD。已有构建目录可传 `-Incremental`，在清除探测缓存后重配置并复用未受影响的对象文件；添加新子项目时须省略该开关，完整重配置以发现新选项。
4. 将全部非系统运行时 DLL 登记到 `native-dependencies.lock.json`；不得只登记顶层三个 DLL。
5. 运行 `finalize-native-manifest.ps1` 回填 SHA-256。脚本发现未登记 DLL 时会失败。
6. 构建应用后，对输出目录运行 `test-native-closure.ps1` 和 `test-angle.ps1`。

网络访问遵循当前进程的 `HTTP_PROXY`、`HTTPS_PROXY` 和 `ALL_PROXY`；脚本不会修改系统代理。

## D3D11 硬件解码互操作

`egl-angle=enabled` 和 `d3d-hwaccel=enabled` 将 mpv 0.41 的 `d3d11-egl` 互操作编入 DLL，允许通过 ANGLE 的 EGLStream 直接采样 NV12 / P010 解码纹理。`egl-angle-lib` 和 `egl-angle-win32` 继续关闭；应用使用已有 Render API 与自建 Composition SwapChain。

`mpv-angle-loaded-module.patch` 将上游的 DLL 搜索替换为 `GetModuleHandleW`，仅复用宿主已校验并加载的 ANGLE。宿主必须在创建 mpv Render context 前初始化 ANGLE，并在进程生存期持有 DLL 引用。硬解策略使用 `d3d11va`，失败时回退软件解码，不启用任何 `*-copy` 模式。

`mpv-angle-device-query-extension.patch` 修正 mpv 0.41 将 `EGL_EXT_device_query` 只在 display 扩展集合中查找的问题。该扩展在 Khronos 规范中属于 client 扩展，锁定 ANGLE 只通过 `eglQueryString(EGL_NO_DISPLAY, EGL_EXTENSIONS)` 声明；补丁同时接受 client 和旧 display 集合，并继续执行原有 D3D11、EGLStream、GL 格式和设备能力校验。

`mpv-demux-seek-skip-to-keyframe.patch` 新增 `--demuxer-skip-to-keyframe` 选项（默认关闭，应用固定开启）。libavformat 的 MPEG-TS 没有关键帧索引，底层 seek 用时间戳二分落在 GOP 中间；mpv 0.41 只在缓存内 seek 找不到目标包时才等到关键帧，对新鲜的 demuxer seek 会把落点之后的非关键帧直接交给解码器，产生成批 `Could not find ref with POC` 与 `Skipping invalid undecodable NALU` 日志及跳转后的短暂丢帧。补丁在 `queue_seek()` 为已经出现过关键帧的选中视频流设置 `skip_to_keyframe`，由既有的 `add_packet_locked()` 逻辑丢弃关键帧之前的包；4096 包预算防止永不产生关键帧的流饿死，反向播放与刷新 seek 不受影响。配合 `--hr-seek-demuxer-offset=1` 让目标之前存在关键帧，hr-seek 仍精确到达目标帧。

2026-08-30 已在 Visual Studio Community 2026、Windows SDK 10.0.26100.0、Clang/LLD 23.0.0git 和 Meson 1.9.2 上完成真实构建。应用输出闭包为 `libmpv-2.dll`、`libEGL.dll`、`libGLESv2.dll`、`d3dcompiler_47.dll` 和清单 JSON；两个烟雾测试均通过。
