# P0-01 项目骨架与抽象边界验证记录

| 字段 | 内容 |
|---|---|
| 日期 / 时间 | 2026-08-28 22:33:51 +08:00 |
| 工作包 | P0-01 目标项目骨架与抽象边界 |
| 起始提交 | `ee59c7f` |
| 工作包提交 | 待提交后回填 |
| 环境 | Windows 11 10.0.26200 x64；.NET SDK 10.0.400；MSBuild 18.9.6 |
| 结论 | 通过 |

## 变更摘要

- 新增 `MpvShell.Player.LibMpv`、`MpvShell.Rendering.WinUI` 及对应测试项目。
- 解决方案和所有项目固定为 x64；默认配置和显式 `Platform=x64` 均可构建。
- `IPlayerBackend.InitializeAsync` 不再接收 HWND、`nint` 或其他视频宿主参数。
- `IMpvPlayerSession` 表达唯一 libmpv core 的生命周期所有权，不暴露原生句柄。
- `IVideoSurfaceRenderer` 表达共享 session、`SwapChainPanel` attach/resize/detach 生命周期，并明确不取得 session 所有权。
- 旧 Sidecar 的 HWND 通过 `LegacyMpvHost` 留在旧项目和 App 过渡组合中；新项目不引用旧 Sidecar 或 VideoHost。
- 没有加载 DLL、创建 mpv 会话、创建 SwapChain 或伪造技术成功状态。

## 命令与结果

### 解决方案项目列表

```powershell
dotnet sln mpv-winui.slnx list
```

- 退出码：`0`
- 结果：目标 4 个生产项目和 4 个目标测试项目均存在；过渡期 Sidecar/VideoHost 及其测试继续保留。

### 显式 x64 构建

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64 --no-restore
```

- 退出码：`0`
- 结果：12 个项目构建通过，`0` 警告，`0` 错误。

最终验证前曾有一次失败：新增 Abstractions 架构守卫测试时，FluentAssertions 表达式树不接受 `is not null` 模式，构建以 `CS8122` 退出。测试改为先用 LINQ 计算违规引用集合、再断言集合为空；重新执行同一构建命令后通过。产品项目在该次失败中均已成功构建。

实际属性复核：

```powershell
dotnet msbuild src/MpvShell.App/MpvShell.App.csproj -p:Platform=x64 -getProperty:Platform,PlatformTarget,Platforms,RuntimeIdentifier,PlatformName
dotnet msbuild src/MpvShell.Player.LibMpv/MpvShell.Player.LibMpv.csproj -p:Platform=x64 -getProperty:Platform,PlatformTarget,Platforms,RuntimeIdentifier,PlatformName
```

- 退出码：`0`
- App 与 LibMpv 均为 `Platform=x64`、`PlatformTarget=x64`、`Platforms=x64`、`PlatformName=x64`。

### 显式 x64 测试

```powershell
dotnet test mpv-winui.slnx -p:Platform=x64 --no-build --no-restore
```

- 退出码：`0`
- 结果：`33/33` 通过。
- `MpvShell.App.Tests`：`16`
- `MpvShell.Player.Abstractions.Tests`：`2`
- `MpvShell.Player.LibMpv.Tests`：`2`
- `MpvShell.Rendering.WinUI.Tests`：`7`
- `MpvShell.Player.MpvSidecar.Tests`：`5`
- `MpvShell.Interop.VideoHost.Tests`：`1`

### 默认配置

```powershell
dotnet build mpv-winui.slnx --no-restore
dotnet test mpv-winui.slnx --no-build --no-restore
```

- 两条命令退出码均为 `0`。
- 构建：`0` 警告，`0` 错误。
- 测试：`33/33` 通过。
- 解决方案只声明 x64 平台，默认配置与显式 x64 配置语义一致。

### 架构边界扫描

```powershell
rg -n "hostHandle|HWND|MpvSidecar|VideoHost" src/MpvShell.Player.Abstractions src/MpvShell.Player.LibMpv src/MpvShell.Rendering.WinUI
```

- `rg` 退出码：`1`（无匹配）。
- 结论：正式抽象和新项目中不存在 HWND 参数或旧项目引用。

## 自动化未覆盖

- 本工作包只定义项目和生命周期边界，没有真实原生资源或视觉输出，因此不需要硬件人工验证。
- Sidecar/VideoHost 仍是 App 的临时运行路径，将在 Gate A～D 通过后的 P0-11 移除；本工作包没有把它们声明为正式后端。
