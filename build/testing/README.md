# 本机 HTTP 播放验证

当前工作包与验收标准见 [Phase 1 计划](../../docs/implementation/phase-1-plan.md)，可用 ZIP、启动方式和发布登记见 [当前发布包入口](../../docs/implementation/release-status.md)。本页说明测试工具，不指定最新产物目录。

`serve-media.py` 仅依赖 Python 标准库，把指定的本地 MP4 文件发布到 `127.0.0.1`，便于使用应用的 URL 输入验证 HTTP 播放和跳转。它只提供 `/sample.mp4`，支持完整 GET、HEAD 和单段字节 Range，不提供目录浏览。

在仓库根目录运行（`python` 应指向已安装的 Python 3）：

```powershell
python build/testing/serve-media.py 'C:\Users\a1426\Downloads\result.mp4'
```

将 `http://127.0.0.1:8765/sample.mp4` 粘贴到播放器中。可通过 `--port 8766` 更换端口，或 `--port 0` 自动选择空闲端口；实际地址会打印到终端。按 Ctrl+C 停止服务。

手动检查响应头和 Range：

```powershell
curl.exe --noproxy '*' --head http://127.0.0.1:8765/sample.mp4
curl.exe --noproxy '*' --range 0-31 --dump-header - --output NUL http://127.0.0.1:8765/sample.mp4
```

完整响应应为 `200`，并带有 `Accept-Ranges: bytes` 和文件总长度；第二条命令应得到 `206`、`Content-Length: 32` 和对应的 `Content-Range`。

播放器验证应至少包含：加载后出现真实视频、播放时间连续增加、暂停/继续、向前及向后跳转、停止并重新加载、播放中关闭窗口。HTTP 服务的响应正确只证明测试输入可用，不能代替播放器运行验证。

## 应用内 GPU 故障恢复验证

`test-app-recovery.ps1` 构建 Debug 应用并通过真实 WinUI 窗口运行故障恢复检查；需要交互式 Windows 桌面和可用 GPU，运行时会显示播放器窗口。故障注入参数仅在 Debug 版本启用。默认生成 64×64、30 fps、20 秒的上红下蓝 Y4M 素材，上方带有横向移动的白色标记，依次验证播放中恢复（`playing`）、暂停时恢复（`paused`）、重建后的首帧再次失败（`immediate-failure`）、重建图形设备本身失败（`rebuild-failure`），以及真实窗口尺寸变化中恢复（`resize`）：

```powershell
pwsh -File build/testing/test-app-recovery.ps1
```

应用检查真实帧像素、渲染资源重建和播放状态连续性，并验证后续不可恢复错误和重建失败能到达 `PlayerViewModel`。终态还要求原生媒体暂停、播放入口禁用；音量命令不能清除错误，播放命令和显示器状态变化不能重启媒体/呈现。每个场景使用独立应用进程，结束时释放播放会话、渲染资源及恢复同步锁，关闭窗口并写出 JSON 报告。单个进程限时 90 秒；超时只终止脚本启动的该测试进程。报告必须同时包含 `Status="Passed"` 和布尔值 `ShutdownCompleted=true`；报告缺失或进程非零退出也会使脚本失败。

脚本仅对自行生成的素材传入 `--recovery-test-animated-pattern`，要求播放恢复后相隔至少 0.5 秒的两次像素采样出现变化，以发现时间前进但画面冻结的问题。暂停场景选取第 30 秒或片中点（取较早者），等待原生 seek 结束且位置稳定，再比较恢复前后的实际暂停位置及像素：位置差小于 1ms，归一化 RGB 平均绝对差小于 2/255，相对差小于 2%。自定义视频应在该处有可见内容；不启用运动图案断言。读回请求仅在成功 Present 后完成。

这些场景通过生成故障异常执行真实应用的恢复和错误处理流程，不会触发操作系统 GPU 超时检测与恢复（TDR）或实际显卡驱动重置。

也可使用实际 4K HDR 视频；`-NoBuild` 复用已构建的 Debug 应用，`-Mode` 选择场景：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode paused
```

另有非默认的 `playback` 模式，不注入故障，不强制重绘，间隔读取自然呈现的画面直到 EOF，检查画面变化、硬解模式稳定、正常结束及资源释放。仍受每进程 90 秒上限约束，适用于本次约 75 秒的 LG 样片，不能直接用于任意长片：

```powershell
pwsh -File build/testing/test-app-recovery.ps1 -NoBuild -Mode playback -MediaPath 'C:\Users\a1426\Downloads\LG.4K.HDR.DEMO_OLED.Art.ts'
```

通过 `-AppPath` 可指定 Debug 可执行文件，`-ReportDirectory` 可指定报告目录。默认报告位于 `artifacts/app-recovery/<时间与唯一标识>/`；每次运行保留独立素材、场景报告和汇总，不覆盖已有文件。使用 HDR 文件通过恢复测试说明该输入的应用播放与恢复流程可用；显示器实际 HDR 输出和颜色准确性仍需另外验证。
