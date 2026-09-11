# RTX 3070 外机测试

这是 Windows 10/11 x64 的 Release 自包含测试包（最低 Windows 10 1809，内部版本 17763），使用启动脚本开启 debug 日志；目标机不需要安装 .NET SDK、PowerShell 7 或使用管理员权限。

1. 将整个压缩包解压到本地目录，保留所有文件和子目录。
2. 双击 `Start-Debug.cmd` 启动播放器，保留随后的命令窗口。也可以把一个视频文件拖到 `Start-Debug.cmd` 上启动播放。
3. 若附带 `TestMedia`，先用其中的 HEVC HDR10、AV1 SDR、FFV1 PQ 渐变短样片检查播放，再使用自己的高码率长视频进行持续测试。检查暂停/继续、跳转、切换文件、窗口缩放、全屏与退出；使用 HDR 显示器时同时记录 Windows HDR 开关状态。
4. 在 `Test-Notes.txt` 填写测试时间、视频文件名、操作步骤、可见问题和大致出现时间。可以附上截图或录屏，放进对应的 `logs` 运行目录；日志本身不能完整反映画面是否正常。
5. 关闭播放器，等待启动窗口显示退出码，然后双击 `Collect-Logs.cmd`。将生成的 `MpvShell-RTX3070-Logs-*.zip` 带回开发机，供 agent 读取检查。

请每次通过 `Start-Debug.cmd` 启动，才能启用本包的 debug 日志和环境记录。每次运行独立保存在包内 `logs/日期时间-唯一标识/`，包含应用日志、Windows/显卡驱动信息、构建清单及进程退出码。包目录无法写入时，会自动存到 `%LOCALAPPDATA%\MpvShell\test-runs\本包标识\logs`，收集脚本会一起查找。

日志压缩包默认生成在解压目录旁，若不可写则改用包目录或上述 LocalAppData 目录；实际路径会显示在收集窗口中。收集不会删除原日志，可以完成多轮测试后统一收集。日志包含本地媒体文件名、系统及显卡信息；`Test-Notes.txt` 的内容也会一并收集。

若播放器闪退或没有打开，请保留启动窗口中的错误提示，并尝试收集日志。若提示没有运行记录，请记录该提示和具体启动错误，连同测试包中的 `build-info.json` 带回。该测试包不代表已经通过 RTX 3070 或真实 HDR 输出验证。

需要脚本自动调用时，可使用系统 Windows PowerShell 执行 `Start-Debug.ps1 -MediaPath '视频绝对路径'` 或 `Collect-Logs.ps1`；这两个 PowerShell 脚本不会暂停等待按键。
