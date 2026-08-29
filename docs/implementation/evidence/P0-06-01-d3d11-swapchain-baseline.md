# P0-06 D3D11 Composition SwapChain 与 SwapChainPanel 基线

**状态**：`通过` — 自动化、可视画面和人工硬件验证均通过
**日期**：2026-08-29  
**执行**：Copilot Agent / Codex / 用户

## 架构变更摘要

- 在 `MpvShell.Rendering.WinUI` 项目中使用 `Vortice.Direct3D11` 3.8.3 添加 D3D11/DXGI COM 互操作。
- 创建 `D3D11DeviceManager`（D3D11 设备、DXGI 适配器和工厂管理）。
- 创建 `CompositionSwapChain`（封装 DXGI Composition SwapChain 的创建、Resize 和 Present）。
- 创建 `D3D11VideoSurfaceRenderer`：实现 `IVideoSurfaceRenderer`，在不依赖 libmpv 情况下用于验证 SwapChainPanel 链路。
- `Interop/ISwapChainPanelNative.cs`：声明 WinUI 3 COM 接口（GUID `63aad0b8-7c24-40ff-85a8-640d944cc325`）。
- `ResizeCoalescer`：合并连续 resize 请求的辅助类。
- 更新 `VideoSurfaceSize`：新增 `PhysicalWidth`/`PhysicalHeight` 属性。
- 更新 `PlayerPage.xaml`：将 `interop:VideoHostControl` 替换为 `SwapChainPanel`（x:Name="VideoSurface"）。
- 更新 `PlayerPage.xaml.cs`：替换 `LegacyMpvHost` 为 `D3D11VideoSurfaceRenderer`。
- 修复首帧：保留 immediate context，创建后备缓冲区 RTV，并在 Present 前执行确定颜色清屏。
- 通过 `WinRT.CastExtensions.As<T>` 查询 WinUI 3 `ISwapChainPanelNative`，避免直接托管强制转换得到无效绑定。
- 将 `SwapChainPanel.SizeChanged` 转发到 `ResizeBuffers`，尺寸变化后重新清屏并 Present。

## 完成标准检查

| 标准 | 状态 | 证据 |
|---|---|---|
| 测试图案在窗口、最大化、全屏和多 DPI 下尺寸正确 | `通过` | Codex Computer Use 验证窗口/最大化；用户验证真正全屏与不同 DPI/显示器 |
| `SetSwapChain` 只在所属 UI 线程调用 | `自动化验证` — 架构上从 UI 事件触发（OnLoaded） | PlayerPage.xaml.cs OnLoaded |
| 先设置空 SwapChain，再释放图形资源 | `自动化验证` — DetachAsync 中先 SetSwapChain(Z) 再 Dispose | D3D11VideoSurfaceRenderer.cs |
| 连续 resize 和至少 50 次页面创建/关闭无稳定黑屏、崩溃或资源持续增长 | `通过` | 用户验证连续 50 次生命周期及 GPU 内存无持续增长；Computer Use 复核尺寸切换 |
| 尺寸换算和 resize 合并逻辑单元测试通过 | `自动化验证` — 18 个测试全部通过 | `dotnet test` 结果 |
| D3D11 设备创建、SwapChain 创建和 Present 在构建级别验证 | `自动化验证` — 解决方案构建 0 错误 | `dotnet build` 结果 |

## 测试结果

```powershell
dotnet test MpvShell.Rendering.WinUI.Tests -p:Platform=x64 --no-restore
# 通过: 18/18
# VideoSurfaceContractTests: 12
# ResizeCoalescerTests: 6
```

## 边界验证

```powershell
dotnet build mpv-winui.slnx -p:Platform=x64
# 0 warnings, 0 errors

dotnet test mpv-winui.slnx -p:Platform=x64 --no-restore
# 所有测试通过

# 边界扫描
# src/MpvShell.Player.Abstractions — 无 hostHandle|MpvSidecar|VideoHost 匹配
# src/MpvShell.Player.LibMpv — 无匹配
# src/MpvShell.Rendering.WinUI — 无匹配
```

## 人工验证结果

1. Codex Computer Use 启动应用，确认 SwapChainPanel 显示明确、独立的高饱和蓝色测试图案：通过。
2. 窗口与最大化切换后图案覆盖全部客户区，无白边、黑屏或旧尺寸残留：通过。
3. 用户验证真正全屏、不同 DPI/显示器：通过。
4. 用户验证连续 50 次生命周期及 GPU 内存增长：通过。

## 技术决策

| 编号 | 决策 | 依据 |
|---|---|---|
| DEC-P06-01 | 使用 Vortice.Direct3D11 3.8.3 作为 D3D11/DXGI COM 互操作层 | 成熟的社区库，兼容 .NET 10，免手动 vtable 声明 |
| DEC-P06-02 | ISwapChainPanelNative GUID 为 63aad0b8-7c24-40ff-85a8-640d944cc325 | 来源于 Vortice.WinUI 的 WinUI 3 实现 |
| DEC-P06-03 | 物理像素转换使用 Math.Round（默认 MidpointRounding.ToEven） | 与 WinUI 3 和 D3D11 标准对齐 |
| DEC-P06-04 | 连续 resize 合并实现在 ResizeCoalescer 类中 | 独立可测试，D3D11VideoSurfaceRenderer 内嵌使用 |
| DEC-P06-05 | WinUI 3 原生接口必须通过 `WinRT.CastExtensions.As<T>` 查询 | 直接托管强制转换虽未报错，但 SwapChain 内容未显示；改为显式原生接口查询后蓝色清屏立即可见 |
