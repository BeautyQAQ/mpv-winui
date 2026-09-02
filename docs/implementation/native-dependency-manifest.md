# 原生依赖清单（Native Dependency Manifest）

> 状态：P0-02 通过（真实构建、依赖闭包、哈希与加载烟雾测试均已验证）
> 架构基线：`docs/architecture.md` v1.1
> 适用范围：`win-x64` 固定 RID 目录中随应用分发的全部原生文件

## 1. 规则

1. 只有在本清单中登记、且 SHA-256 校验通过的原生文件，才允许放入 `runtimes/win-x64/native/` 运行时目录。
2. 来源不明的二进制（无版本、无构建参数、无许可证说明）一律不得登记、不得分发。
3. 任何文件增删、版本变更必须同步更新本清单，并在 `phase-0-progress.md` 追加验证记录。
4. 哈希在文件落盘后计算并回填；校验失败时 `MpvShell.Player.LibMpv` 的确定性加载器必须稳定失败并给出可诊断错误（见 P0-02）。

## 2. 已锁定源码

| 组件 | 上游版本 | 不可变 commit | 构建/依赖锁 | 许可证结论 |
|---|---|---|---|---|
| mpv | `v0.41.0` | `41f6a645068483470267271e1d09966ca3b9f413` | Client API 2.5；Clang/LLD 23；静态 CRT；关闭 cplayer、GPL、build-date 和未审计可选功能 | LGPL-2.1-or-later；实际静态链接闭包见下表 |
| ANGLE | Chrome 152 稳定线 `chromium/7977` | `736ed80c7552a4b267bd54a282b971aa4555cb3e` | DEPS SHA-256 `2B602E3E2E4D602DFB1B54E704A5F3493788BBFC787F041D401CDAF68FC8DF6D`；MSVC；D3D11-only | BSD-3-Clause；另随附 Windows SDK 可再分发的 D3DCompiler 47 |

mpv 同 commit 头文件哈希：

| 文件 | SHA-256 |
|---|---|
| `include/mpv/client.h` | `A36A8D809FF068166676BF7C98FA1EC2EF5EB7C591A3359D5AA0E4D60E5DE40E` |
| `include/mpv/render.h` | `1FE12292DE3E79D64FFF205CFDC40EE36EE5F5B52D2BF88292DC121326F26C94` |
| `include/mpv/render_gl.h` | `33CCC48EBD32437DA46673FAEA034B5C46EC2DCFC587E9655A5017BF8576B5B8` |

## 3. 二进制清单表

| 文件 | 逻辑库名 | 版本 | 架构 | 来源（URL/自建脚本） | 构建参数摘要 | 许可证 | 依赖闭包 | SHA-256 | 登记日期 |
|---|---|---|---|---|---|---|---|---|---|
| `libmpv-2.dll` | `mpv` | v0.41.0 / `41f6a645…` | x64 | `build/native/build-mpv.ps1` | Clang/LLD 23、Release、`/MT`、LGPL-only、静态第三方库 | LGPL-2.1-or-later；静态组件见下表 | 仅 Windows 系统 DLL；无 VC Runtime/第三方 DLL | `D0D01C2AF708423E6B281ADC58A39FAFD1FB2CFA8FE76D437D76383F088BA228` | 2026-08-30 |
| `d3dcompiler_47.dll` | — | Windows SDK 10.0.26100.0 | x64 | ANGLE GN 构建输出 | ANGLE D3D11-only | Microsoft Windows SDK 可再分发组件 | `KERNEL32`、`ADVAPI32`、`RPCRT4` | `E407B9FFADEF47E87994DC488214F75405CF04EE99589F2315FC7AB99FD1DFE2` | 2026-08-30 |
| `libGLESv2.dll` | `GLESv2` | `chromium/7977` / `736ed80c…` | x64 | `build/native/build-angle.ps1` | MSVC Release、D3D11-only | BSD-3-Clause | Windows 系统 DLL | `55ECF05D47B4CCFCF91B4F5FE0F21ED3FCE2584B6CC54E4F6855A65142F32D3C` | 2026-08-30 |
| `libEGL.dll` | `EGL` | `chromium/7977` / `736ed80c…` | x64 | 同上 | 同上 | BSD-3-Clause | `KERNEL32`；运行时使用同组 GLES/D3DCompiler | `8B912D254D5FC8755840B6FD1E404F36E466521FD63F68255528CD7F101909D7` | 2026-08-30 |

## 4. libmpv 静态链接源码闭包

| 组件 | 锁定版本/commit | 许可证 | 作用 |
|---|---|---|---|
| FFmpeg | 8.0.3 / `74d461d3ed8a9fdd956336fd2a6a77ebc1bb91a9` | LGPL-2.1-or-later（GPL/nonfree 均关闭） | 解封装、编解码、滤镜、D3D 硬解 |
| libplacebo | 7.351.0 / `3188549fba13bbdf3a5a98de2a38c2e71f04e21e` | LGPL-2.1-or-later | GPU 色彩与着色器路径 |
| libass | 0.17.4 / `bbb3c7f1570a4a021e52683f3fbdf74fe492ae84` | ISC | ASS 字幕 |
| FreeType | 2.14.3 / 源码哈希见 `source-lock.json` | FTL OR GPL-2.0-or-later（采用 FTL） | 字体栅格化 |
| FriBidi | 1.0.16 / 源码哈希见 `source-lock.json` | LGPL-2.1-or-later | 双向文本 |
| HarfBuzz | 13.0.1 / 源码哈希见 `source-lock.json` | MIT | 文本塑形 |
| zlib | 1.3.2 / 源码哈希见 `source-lock.json` | Zlib | 压缩支持 |
| Vulkan-Headers | `cacef3039d277c448c89336290ec3937270b0996` | Apache-2.0 | libplacebo 编译期头文件；Vulkan 运行时后端关闭 |

除明确标注为编译期头文件的 Vulkan-Headers 外，上表组件均静态并入 `libmpv-2.dll`，不会在运行时形成额外 DLL。Meson 还解析了 libpng 1.6.55 的 fallback，但最终 `mpv-2.dll` 链接规则不包含 libpng；其来源哈希仍保留在锁文件中。完整 Meson 参数、工具链版本、源码哈希和三个上游兼容补丁均保存在 `build/native/`。

## 5. 直接导入闭包结论

`dumpbin /dependents` 证明 `libmpv-2.dll` 仅直接依赖 Windows 系统组件：`GDI32`、`USER32`、`ole32`、`AVRT`、`dwmapi`、`IMM32`、`ntdll`、API Set、`UxTheme`、`OPENGL32`、`KERNEL32`、`SHELL32`、`ADVAPI32`、`bcrypt`、`SHLWAPI`、`WS2_32`、`ncrypt`、`CRYPT32`、`Secur32`。未发现 `VCRUNTIME`、`MSVCP` 或未登记第三方 DLL。

## 6. 变更历史

| 日期 | 工作包 | 变更 | 提交 |
|---|---|---|---|
| 2026-08-28 | P0-00 | 建立清单格式与登记规则（空表，无二进制落盘） | b2f4ccf |
| 2026-08-29 | P0-02 | 锁定 mpv/ANGLE 源码、API/DEPS 哈希和构建入口；实际二进制仍未落盘 | （待提交） |
| 2026-08-30 | P0-02 | 完成真实 mpv/ANGLE 构建、四文件运行时闭包、许可证与 PE 导入审计、哈希回填和输出目录烟雾测试 | （待提交） |
