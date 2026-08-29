# 原生依赖清单（Native Dependency Manifest）

> 状态：P0-02 进行中（源码已锁定；实际 DLL 闭包与哈希待原生工具链构建）
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
| mpv | `v0.41.0` | `41f6a645068483470267271e1d09966ca3b9f413` | `build/native/source-lock.json`；Client API 2.5；关闭 cplayer、GPL、build-date、脚本和可选未审计功能 | mpv 目标为 LGPL-2.1-or-later；最终结论仍取决于 FFmpeg、libplacebo、libass 等实际链接闭包 |
| ANGLE | Chrome 152 稳定线 `chromium/7977` | `736ed80c7552a4b267bd54a282b971aa4555cb3e` | DEPS SHA-256 `2B602E3E2E4D602DFB1B54E704A5F3493788BBFC787F041D401CDAF68FC8DF6D`；仅启用 D3D11 | BSD 3-Clause；最终随附文件以该 commit 的 DEPS/CIPD 闭包为准 |

mpv 同 commit 头文件哈希：

| 文件 | SHA-256 |
|---|---|
| `include/mpv/client.h` | `A36A8D809FF068166676BF7C98FA1EC2EF5EB7C591A3359D5AA0E4D60E5DE40E` |
| `include/mpv/render.h` | `1FE12292DE3E79D64FFF205CFDC40EE36EE5F5B52D2BF88292DC121326F26C94` |
| `include/mpv/render_gl.h` | `33CCC48EBD32437DA46673FAEA034B5C46EC2DCFC587E9655A5017BF8576B5B8` |

## 3. 二进制清单表（待构建回填）

| 文件 | 逻辑库名 | 版本 | 架构 | 来源（URL/自建脚本） | 构建参数摘要 | 许可证 | 依赖闭包 | SHA-256 | 登记日期 |
|---|---|---|---|---|---|---|---|---|---|
| `libmpv-2.dll` | `mpv` | v0.41.0 / `41f6a645…` | x64 | `build/native/build-mpv.ps1` | 见源码锁文件 | 待实际链接闭包审计 | 待构建扫描 | 待构建 | — |
| `libEGL.dll` | `EGL` | `chromium/7977` / `736ed80c…` | x64 | `build/native/build-angle.ps1` | D3D11-only GN 参数见源码锁文件 | BSD 3-Clause | 待 gclient/CIPD 解析 | 待构建 | — |
| `libGLESv2.dll` | `GLESv2` | `chromium/7977` / `736ed80c…` | x64 | 同上 | 同上 | BSD 3-Clause | 待 gclient/CIPD 解析 | 待构建 | — |

## 4. 待登记项（随工作包补充）

| 工作包 | 待登记内容 |
|---|---|
| P0-02 | `libmpv-2.dll` 完整依赖闭包（含系统/非系统 DLL 区分）与加载器诊断项 |
| P0-02 | `libEGL.dll`、`libGLESv2.dll` 及 ANGLE 实际携带的其他文件 |

## 5. 变更历史

| 日期 | 工作包 | 变更 | 提交 |
|---|---|---|---|
| 2026-08-28 | P0-00 | 建立清单格式与登记规则（空表，无二进制落盘） | b2f4ccf |
| 2026-08-29 | P0-02 | 锁定 mpv/ANGLE 源码、API/DEPS 哈希和构建入口；实际二进制仍未落盘 | （待提交） |
