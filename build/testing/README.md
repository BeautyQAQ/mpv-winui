# 本机 HTTP 播放验证

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
