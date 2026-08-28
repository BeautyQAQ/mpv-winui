# P0-00 基线验证记录

| 字段 | 内容 |
|---|---|
| 日期 / 时间 | 2026-08-28 22:17:16 +08:00 |
| 工作包 | P0-00 基线固化与外部输入确认 |
| 仓库基线 | `add45b6e9c9634f1b3617013e14ee9e887819c26` |
| 复核提交 | `7be0e82f20c669e93bebddbc0b41897d7147f536` |
| 环境 | Windows 11 10.0.26200 x64；.NET SDK 10.0.400；MSBuild 18.9.6；NVIDIA GeForce GTX 1060 5GB，驱动 32.0.15.8180 |
| 结论 | 通过；显式 `Debug|x64` 配置缺失已作为 P0-01 已知输入保留 |

## 命令与结果

### SDK 与运行时

```powershell
dotnet --info
```

- 退出码：`0`
- SDK：`10.0.400`
- MSBuild：`18.9.6+14fbf8d52`
- RID：`win-x64`
- Host：`.NET 10.0.11 x64`

### 默认配置构建

```powershell
dotnet build mpv-winui.slnx --no-restore
```

- 退出码：`0`
- 结果：通过，`0` 警告，`0` 错误
- 构建项目：4 个生产项目和 4 个测试项目

### 默认配置测试

```powershell
dotnet test mpv-winui.slnx --no-build --no-restore
```

- 退出码：`0`
- 结果：`21/21` 通过
- `MpvShell.Player.Abstractions.Tests`：`1`
- `MpvShell.Player.MpvSidecar.Tests`：`3`
- `MpvShell.Interop.VideoHost.Tests`：`1`
- `MpvShell.App.Tests`：`16`

### 显式 x64 配置

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
```

- 退出码：`1`
- 结果：预期失败，解决方案配置 `Debug|x64` 无效
- 后续处理：P0-01 增加并验证 x64 解决方案配置

### 原生资产扫描

```powershell
$native = Get-ChildItem -LiteralPath . -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' -and
        ($_.Extension -in '.dll', '.lib', '.a' -or $_.FullName -match '[\\/]runtimes[\\/]')
    }
```

- 退出码：`0`
- 结果：`bin/`、`obj/`、`.git/` 之外没有原生运行时资产
- 结论：没有把来源不明的 DLL 或静态库作为 Phase 0 输入

## 未验证项

- HDR 显示器和完整 HDR 验证机尚未确认，保持为 `EXT-07` 阻塞项。
- 原生依赖尚未构建或落盘；版本、依赖闭包、许可证和 SHA-256 由 P0-02 验证并登记。
- ANGLE 的精确 commit、DEPS/CIPD 闭包和 GN 参数尚未确定，保持为 `EXT-03` 阻塞项。
