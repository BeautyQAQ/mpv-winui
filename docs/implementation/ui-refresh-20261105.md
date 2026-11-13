# 播放界面调整（2026-11-05）

按用户反馈移除黑紫色固定主题，默认跟随 Windows 的应用浅色 / 深色模式。此改动基于 `da774f3`，属于该提交之后的界面调整，不改写此前封存的 Phase 3 候选或验收结果。

## 界面变化

- 使用 WinUI 主题画刷统一空闲画布、控制条、状态提示和信息面板；强调色用于主要操作、进度和选中状态。
- 系统标题栏随页面实际主题同步，保留原生窗口拖动、缩放与关闭行为。
- 首页保留本地文件和链接两个入口；链接输入改为原生 Flyout，不再持续占用顶部空间。
- 播放后显示图标控制条：播放 / 暂停、相对定位、停止、倍速、音量、字幕音轨、媒体信息和全屏。图标保留中文辅助技术名称及悬停提示。
- 宽窗口单行排列；紧凑窗口自动分行。最近打开移至顶部，空闲时仍可访问。
- 新增播放 / 静音图标派生属性，以及空闲背景与控制条可见性；补充生命周期、自动隐藏、图标状态及通知测试。
- 链接 Flyout 接入现有自动隐藏和 Esc 仲裁，输入期间不触发底层播放快捷键。
- 中文内容明确使用 `Microsoft YaHei UI`，图标使用 WinUI 的符号字体。

## 验证

- Debug / Release x64 最终构建：均为 0 警告、0 错误。
- Debug 解决方案回归：558 通过、0 失败、11 跳过；随后仅调整字体、图标、边框及标题栏主题，并重新构建。
- 最终 Release 解决方案回归：542 通过、0 失败、11 跳过。
- 11 项跳过属于默认环境未启用的专用媒体 / 硬件测试，不视为这些项目通过，也不替代发布候选的显式媒体验收。
- 实际窗口检查：首页、链接弹窗播放、最近记录播放、暂停图标、播放时自动隐藏、约 720 DIP 紧凑窗口、媒体信息、字幕音轨面板、F11 全屏及 Esc 返回。
- 浅色使用系统默认模式；深色通过临时构建主题覆盖检查，未修改 Windows 设置。覆盖已撤回，最终 Debug / Release 均跟随系统。尚未单独执行运行中切换 Windows 主题或高对比度模式的人工验收。
- 验证日志、TRX 和截图：`artifacts/ui-refresh-20261105/`。GUI 验证进程均正常退出；测试生成的最近记录移至证据目录，恢复测试前无历史文件的状态。最终预览窗口留给用户查看。

## 后续候选交付（2026-11-13）

上述 UI 已随干净 `2d44dbd` 的独立候选 `MpvShell-phase3-ui-candidate-20261113-233547-b4d96c` 交付，详见 [本轮发布验收](evidence/P3-03-01-ui-candidate-20261113.md)。当前候选实际 DLL 显式回归为 551/0/2，两项跳过为 RTX 3070 不适用的旧 Intel 场景；严格 runner 原结果保留。最终目录同一进程在空闲首页切换 Windows 浅色/深色，首页和标题栏响应，链接弹窗分别重开检查；已加载暂停媒体的同一轨道面板在水生/沙漠高对比主题下检查悬停和选中可读性，沙漠主题另验 Tab 焦点，无需修改产品代码。主题和原先不存在的最近记录均已恢复，两次 GUI 正常退出。该轮补齐上述主题场景，但没有验证播放中切换浅深色、已打开链接弹窗动态切换、窄窗或完整辅助功能矩阵；11 月 5 日历史记录与旧候选保持不变。

## 原生样式参考

- [Windows 字体规范](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
- [Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font)
- [AppWindowTitleBar.PreferredTheme](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowtitlebar.preferredtheme?view=windows-app-sdk-1.8)
