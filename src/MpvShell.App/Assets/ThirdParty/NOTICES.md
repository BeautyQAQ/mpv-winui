# 原生组件第三方通知

本目录随 MpvShell 的构建输出和发布包提供。各子目录保留上游许可证及版权说明原文；`license-origins.json` 记录原文件路径与 SHA-256。下表描述本项目使用的源码和构建配置，具体条款见原文。

| 组件 | 固定源码 | 随附原文 |
|---|---|---|
| mpv v0.41.0 | [mpv-player/mpv](https://github.com/mpv-player/mpv/tree/41f6a645068483470267271e1d09966ca3b9f413)；`41f6a645068483470267271e1d09966ca3b9f413` | `mpv/Copyright`、`mpv/LICENSE.LGPL` |
| FFmpeg 8.0.3 | [GStreamer Meson port](https://gitlab.freedesktop.org/gstreamer/meson-ports/ffmpeg/-/tree/74d461d3ed8a9fdd956336fd2a6a77ebc1bb91a9)；`74d461d3ed8a9fdd956336fd2a6a77ebc1bb91a9` | `ffmpeg/LICENSE.md`、`ffmpeg/COPYING.LGPLv2.1` |
| dav1d 1.5.4 | [VideoLAN dav1d](https://code.videolan.org/videolan/dav1d/-/tree/54706fc6bc0cdecab7e9593974a4039cc038fca7)；`54706fc6bc0cdecab7e9593974a4039cc038fca7` | `dav1d/COPYING`（BSD-2-Clause） |
| libass 0.17.4 | [libass/libass](https://github.com/libass/libass/tree/bbb3c7f1570a4a021e52683f3fbdf74fe492ae84)；`bbb3c7f1570a4a021e52683f3fbdf74fe492ae84` | `libass/COPYING` |
| libplacebo 7.351.0 | [VideoLAN libplacebo](https://code.videolan.org/videolan/libplacebo/-/tree/3188549fba13bbdf3a5a98de2a38c2e71f04e21e)；`3188549fba13bbdf3a5a98de2a38c2e71f04e21e` | `libplacebo/LICENSE` 及其子目录 |
| FreeType 2.14.3 | [上游源码包](https://download.savannah.gnu.org/releases/freetype/freetype-2.14.3.tar.xz) | `freetype/LICENSE.TXT`、`freetype/FTL.TXT` 及 BDF/PCF 原说明 |
| FriBidi 1.0.16 | [上游源码包](https://github.com/fribidi/fribidi/releases/download/v1.0.16/fribidi-1.0.16.tar.xz) | `fribidi/COPYING` |
| HarfBuzz 13.0.1 | [上游源码包](https://github.com/harfbuzz/harfbuzz/releases/download/13.0.1/harfbuzz-13.0.1.tar.xz) | `harfbuzz/COPYING`、`harfbuzz/ms-use/COPYING` |
| zlib 1.3.2 | [上游源码包](https://zlib.net/zlib-1.3.2.tar.xz) | `zlib/LICENSE` |
| ANGLE chromium/7977 | [ANGLE 源码](https://chromium.googlesource.com/angle/angle/+/736ed80c7552a4b267bd54a282b971aa4555cb3e)；`736ed80c7552a4b267bd54a282b971aa4555cb3e` | `angle/LICENSE` 及其子目录 |
| Windows SDK 10.0.26100.0 | 构建机 SDK 的 `Licenses/10.0.26100.0`；随 ANGLE 输出携带 `d3dcompiler_47.dll` | `windows-sdk/sdk_license.rtf`、`windows-sdk/sdk_third_party_notices.rtf` |

FreeType 源码包 SHA-256：`36bc4f1cc413335368ee656c42afca65c5a3987e8768cc28cf11ba775e785a5f`。FriBidi：`1b1cde5b235d40479e91be2f0e88a309e3214c8ab470ec8a2744d82a5a9ea05c`。HarfBuzz：`3553d943401c34ab9b8c75f35cdb8452ca660233b0e9d4a22395ce5245484bd7`。zlib：`d7a0654783a4da529d1bb793b7ad9c3318020af77667bcae35f95d0e42a792f3`。

libplacebo 随附子模块原文包括 fast_float（`1bf70101536d37fa9954cc4f03fd0903d045a9f3`）、glad（`73db193f853e2ee079bf3ca8a64aa2eaf6459043`）与 Vulkan-Headers（`cacef3039d277c448c89336290ec3937270b0996`，编译期头文件）。ANGLE 子目录保留其锁定源码/DEPS 中的 xxHash、volk、astc-encoder、zlib、JsonCpp、Vulkan-Headers、Vulkan-Loader 与 SPIRV-Headers 原文；这些目录也包含编译期头文件的通知，不表示启用了 Vulkan 运行时后端。

Portions of this software are copyright © 2026 The FreeType Project (https://freetype.org). All rights reserved.

This software is based in part on the work of the Independent JPEG Group. FFmpeg 的 `LICENSE.md` 说明了相关来源文件。

## 源码与重建

本应用的[项目仓库](https://github.com/BeautyQAQ/mpv-winui)中，`build/native/README.md` 是原生重建入口；`build/native/source-lock.json` 固定源码、工具链、构建选项与源码包哈希，`build/native/mpv-subprojects/` 固定 Meson 包，`build/native/patches/` 保存本项目的兼容补丁。重建时使用与发布包对应的项目版本，按该 README 先运行 `build-angle.ps1`，再运行 `build-mpv.ps1`，最后按 `finalize-native-manifest.ps1` 和闭包检查更新并验证二进制清单。

`libmpv-2.dll` 将上述 FFmpeg、dav1d、libass、libplacebo 和字体依赖静态链接。mpv 配置 `gpl=false`，FFmpeg 配置 `gpl=disabled`、`nonfree=disabled`；FreeType 按项目清单采用 FTL。dav1d 配置 `default_library=static`、`bitdepths=8,16`、`enable_asm=true`，其中 16 表示同时编入 10/12 bit 路径。FFmpeg 启用 `libdav1d` 与 `libdav1d_decoder`，用于 AV1 软件解码；该能力直接包含在发布的 DLL 中。

dav1d 的 `COPYING` SHA-256 为 `225BBEC3AFC382F1BB85F7BB01655F3A37ED5E693A4FA18A64C0C5BAB48C854E`。原生构建使用 Clang/LLD 23、Meson 1.9.2、静态 CRT 和 NASM 2.16.03；完整参数与工具哈希以对应项目版本的锁文件为准。源码和工具获取只属于开发构建流程；应用运行及本目录复制不需要下载。
