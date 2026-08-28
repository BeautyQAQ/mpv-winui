# 原生依赖清单（Native Dependency Manifest）

> 状态：模板（P0-00 建立格式，P0-02 填入实际值）
> 架构基线：`docs/architecture.md` v1.1
> 适用范围：`win-x64` 固定 RID 目录中随应用分发的全部原生文件

## 1. 规则

1. 只有在本清单中登记、且 SHA-256 校验通过的原生文件，才允许放入 `runtimes/win-x64/native/` 运行时目录。
2. 来源不明的二进制（无版本、无构建参数、无许可证说明）一律不得登记、不得分发。
3. 任何文件增删、版本变更必须同步更新本清单，并在 `phase-0-progress.md` 追加验证记录。
4. 哈希在文件落盘后计算并回填；校验失败时 `MpvShell.Player.LibMpv` 的确定性加载器必须稳定失败并给出可诊断错误（见 P0-02）。

## 2. 清单表

| 文件 | 逻辑库名 | 版本 | 架构 | 来源（URL/自建脚本） | 构建参数摘要 | 许可证 | 依赖闭包 | SHA-256 | 登记日期 |
|---|---|---|---|---|---|---|---|---|---|
| （示例）libmpv-2.dll | libmpv | v0.41.0（固定 commit） | x64 | mpv 官方签名 tag 自建（脚本路径） | Meson：`-Dlibmpv=true -Ddefault_library=shared -Dcplayer=false -Dgpl=false`；完整参数由 P0-02 登记 | LGPL v2.1+ 兼容目标；须审计全部链接依赖 | libmpv-2.dll 及其运行依赖 | （落盘后计算） | （示例） |
| （示例）libEGL.dll | ANGLE | （固定 commit） | x64 | ANGLE 官方源码自建 | GN 参数与 DEPS/CIPD 闭包由 P0-02 登记 | BSD 3-Clause；须审计随附依赖 | libEGL.dll、libGLESv2.dll 及依赖 | （落盘后计算） | （示例） |

## 3. 待登记项（随工作包补充）

| 工作包 | 待登记内容 |
|---|---|
| P0-02 | `libmpv-2.dll` 完整依赖闭包（含系统/非系统 DLL 区分）与加载器诊断项 |
| P0-02 | `libEGL.dll`、`libGLESv2.dll` 及 ANGLE 实际携带的其他文件 |

## 4. 变更历史

| 日期 | 工作包 | 变更 | 提交 |
|---|---|---|---|
| 2026-08-28 | P0-00 | 建立清单格式与登记规则（空表，无二进制落盘） | b2f4ccf |
