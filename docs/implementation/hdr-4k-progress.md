# HDR / 4K 硬解实施与晚间验收

日期：2026-09-11。用户已恢复 HDR/4K 硬解工作，并安排晚间亲自测试。本文记录实现、自动化证据、可再生成的测试输入与人工验收方法；真实 HDR 显示效果与跨屏结论仍需晚间运行记录补齐。

## 本轮范围与状态

- 显示器检测读取 Windows 当前窗口的 Advanced Color 状态，区分“设备支持 HDR”和“Windows 已启用 HDR”，并获取系统 SDR 白亮度、峰值亮度和输出名称。查询失败保留未知状态。
- HDR 渲染、硬解配置、应用集成与最终 Debug/Release 回归由本轮主任务统一记录；素材校验通过不等于这些项目通过。
- 下述素材全部在本机生成，不使用用户私人视频，不依赖公网媒体地址。媒体、工具和原始报告位于 Git 忽略的 `artifacts/hdr-4k/`；仓库只保存生成器、校验脚本与记录。

## 输出路线与实现边界

正式代码根据片源和系统状态一起选择输出：SDR 片源始终使用 BGRA8/sRGB；PQ 或 HLG 片源在 Windows HDR 开启时使用 RGB10A2 / PQ / BT.2020，否则由 mpv 色调映射到 SDR。片源标签、显示器支持能力、系统当前开启状态、实际输出模式分别记录，不能相互替代。

HDR 目标峰值取系统报告值；缺失时采用明确标记的 1000 nit 默认值。应用设置 mpv 的 target-prim、target-trc、target-peak 和 tone-mapping，传入 Render API 每通道 DEPTH=10，并检查 DXGI 呈现支持后设置 RGB_FULL_G2084_NONE_P2020。XAML 控件与视频使用独立合成表面，SDR 控件白亮度由 Windows 合成处理。

切换色彩输出时先停止呈现、在 UI 线程解除 SwapChainPanel，再经命令线程设置 mpv 色彩、经渲染线程替换 SwapChain/EGL surface，最后重新绑定。停止呈现期间渲染线程仍以跳过绘制的方式响应 mpv 渲染请求（见下文 RTX 3070 跟进）。ANGLE 的 EGL_KHR_no_config_context 允许保留同一 EGL/mpv render context 和暂停视频帧。HDR 输出协商失败时，mpv 色彩与 SwapChain 一起回退到 SDR，不能把 PQ 像素交给 SDR 色彩空间。

候选路径比较：真实 GPU 存储测试覆盖 RGB10A2 的相邻 10-bit 码值，以及 RGBA16F 大于 1 与负数的保留。当前选 PQ10；直接把 mpv vo=gpu 的 linear 输出声明为 scRGB 不成立：Windows scRGB 1.0 对应 80 nit，锁定 mpv 的线性输出归一化依赖目标色彩/峰值，HDR 情况仍以 203 nit 参考白工作，且 BT.2020→BT.709 默认涉及色域映射。正确 scRGB 需要额外亮度和扩展色域处理，因此当前不把“能创建 FP16 纹理”当成 scRGB 支持。

依据：[Microsoft Advanced Color 格式、色彩空间与白亮度](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)、[锁定 mpv shader 色彩转换](https://github.com/mpv-player/mpv/blob/41f6a645068483470267271e1d09966ca3b9f413/video/out/gpu/video_shaders.c)、[锁定 Render API DEPTH](https://github.com/mpv-player/mpv/blob/41f6a645068483470267271e1d09966ca3b9f413/include/mpv/render.h)。GPU 像素测试可以验证编码和通路，HDR 显示器的实际高光、XAML 亮度和跨屏行为仍需下文的真实屏幕验收。

## 原生解码与兼容修复

默认配置为 `hwdec=d3d11va`、`gpu-hwdec-interop=d3d11-egl`，不启用 copy-back。后端查询 `hwdec-current`、`hwdec-interop`、`video-params/pixelformat` 与 `hw-pixelformat`，只有实际返回 D3D11 / d3d11-egl / d3d11 才报告 GPU 纹理传递；软件回退、未知计数和零丢帧分别显示。

本轮真实样本测试发现原生库原先没有编入 ANGLE 硬解互操作。启用后又发现锁定 mpv 在 EGL display 扩展中查找 `EGL_EXT_device_query`，而锁定 ANGLE 在 client 扩展中发布它；补丁兼容两种查询位置，仍要求真实扩展存在。ANGLE 模块从宿主已校验、已加载的 `libEGL.dll` 句柄获取，不引入依赖搜索路径回退。

首次 AV1 样本测试没有输出帧，原因是旧 FFmpeg 构建缺少适用的软件 AV1 解码器。本轮静态集成锁定的 dav1d 1.5.4 与 x64 SIMD，实现无 AV1 硬解能力时的软件回退。最终原生库 SHA-256 为 `932D70710EECAA1D686D1E49674AE9A13C555E6C4701695858B5163A8C5DFAAB`，32,123,392 字节；四 DLL 闭包、20 个 Windows 系统导入和 Client API 2.5 已核验。

另一次原生测试发现 mpv 的 `target-peak` 是带枚举项的整数选项，不能按 Double 设置；已改用经过取整和范围约束的字符串。源码锁与补丁进入仓库，本文保留失败原因及最终复测结果。

## RTX 3070 日志跟进（2026-09-12）

[RTX 3070 分析报告](../../artifacts/rtx3070-analysis/203122-0fec3020/分析报告.md)指出两项问题，本轮处理如下。

**HDR 输出切换时的渲染等待已修复。** 原实现把 `_canPresent=false` 当作“完全不调用 Render API”，重配期间 mpv 的 `flip_page` 每帧等待 200 ms 超时（日志 `mpv_render_context_render() not being called or stuck`），并把该帧计入 `frame-drop-count`。现在“能否呈现到 SwapChain”与“是否响应 mpv 渲染请求”分开：不能呈现时渲染线程仍调用 `Update()`，并用 `MPV_RENDER_PARAM_SKIP_RENDERING` 消费帧、照常 `ReportSwap()`，不绘制也不呈现，因此 PQ 帧不会进入旧的 SDR SwapChain；恢复呈现后由强制重绘补画当前帧。只有图形故障、恢复重建和终态才完全停止调用 Render API。新增集成测试 `RenderContinuityIntegrationTests`：不绑定表面播放并在播放中切换 SDR→HDR10→SDR，要求 `frame-drop-count` 为 0；改动前同一测试测得 9 帧 VO 丢帧。输出重配日志现附带“重配期间跳过呈现 N 帧”，外机复测可直接核对。

**进度停在最后一帧时间戳已修复。** mpv 的 `time-pos` 在 EOF 时停在最后一帧（8 秒 30 fps 素材为 7.967 秒，见 3070 日志），UI 向下取整显示 00:07 / 00:08。后端在 EOF（`eof-reached` 或 EndFile reason 0）时把位置对齐到已知时长；时长未知时保留最后位置。下一次加载由 StartFile 重置为 0。

**LG TS 跳转后的 HEVC 参考帧错误未改代码。** 报告要求用同一文件、同一组目标时间对比顺播、硬解和软解后再决定，现有日志不能区分 TS 随机访问与解码路径问题，因此不禁用硬解、不改 seek 策略。复测时请分别记录三种方式下 `Could not find ref with POC` 的组数和是否肉眼可见花屏。

## 本轮自动化结果

最终 Debug、Release 构建均为 0 警告、0 错误，各通过 **138/138** 测试，无跳过：App 36、LibMpv 43、Rendering 51、Abstractions 2、旧项目 6。已显式提供三份素材路径，因此 HEVC、AV1 与 HDR 梯度测试实际执行；日常不设置素材环境变量时，这三项由 xUnit 明确报告跳过。

原始构建/测试日志位于 `artifacts/hdr-4k/evidence/`，TRX 位于 `artifacts/hdr-4k/test-results/Debug/` 与 `Release/`。真实媒体报告及 mpv 日志按配置保存在 `artifacts/hdr-4k/hardware-reports/Debug/` 与 `Release/`。

| 配置 / 样本 | 实际路径 | 呈现帧 | 解码丢帧 / 呈现丢帧 | 测试总耗时 / CPU 时间 |
|---|---|---:|---:|---|
| Debug HEVC Main10 | D3D11VA → d3d11-egl → d3d11[p010] | 60 | 0 / 1 | 2.203 / 1.125 秒 |
| Release HEVC Main10 | D3D11VA → d3d11-egl → d3d11[p010] | 60 | 0 / 0 | 2.124 / 0.953 秒 |
| Debug AV1 10-bit | dav1d 软件回退，yuv420p10 | 60 | 0 / 3 | 2.351 / 6.234 秒 |
| Release AV1 10-bit | dav1d 软件回退，yuv420p10 | 60 | 0 / 2 | 2.284 / 6.203 秒 |

环境为 Windows 11 10.0.26200、NVIDIA GTX 1060 5GB、驱动 32.0.15.8180、ANGLE D3D11。总耗时包含初始化，CPU 时间是测试进程多线程累计值，不能直接换算为稳定播放占用率。AV1 日志保留当前设备不支持硬解的错误与后续软件输出；最终帧、实际软件状态及非黑画面均已验证。上述短样本不能证明高码率影片、4K60 或长时间零丢帧。

HDR 梯度在两种配置均通过：同一份暂停的 4K PQ 无损片源先验证 11 档参考亮度的 10-bit 码值（每通道误差最多 2 个码值），再检查 1000 nit 目标及 SDR tone mapping 的黑位、单调性和高光。另有 GPU 测试验证相邻 10-bit 码、FP16 扩展范围，以及同一 mpv/EGL 上下文在 SDR/PQ 之间切换并保留暂停帧。生产代码没有 CPU 逐帧读回；读回仅用于测试断言。

复测示例（PowerShell 7，仓库根目录）：

```powershell
$env:MPVSHELL_TEST_HEVC_MEDIA = (Resolve-Path artifacts/hdr-4k/media/hevc-main10-hdr10-4k30.mkv).Path
$env:MPVSHELL_TEST_AV1_MEDIA = (Resolve-Path artifacts/hdr-4k/media/av1-main10-sdr-4k30.mkv).Path
$env:MPVSHELL_TEST_HDR_GRADIENT_MEDIA = (Resolve-Path artifacts/hdr-4k/media/pq-gradient-10bit-4k.mkv).Path
$env:MPVSHELL_TEST_HARDWARE_REPORT_DIR = Join-Path (Get-Location) artifacts/hdr-4k/hardware-reports/Release
dotnet build mpv-winui.slnx -c Release -p:Platform=x64 --no-restore
dotnet test mpv-winui.slnx -c Release -p:Platform=x64 --no-build --no-restore
```

应用启动烟雾检查已验证 SwapChainPanel 绑定、1104×721 resize、真实显示器查询、HDR 片源识别及 Windows HDR 关闭时的 SDR tone mapping，FFV1 文件播放到 EOF。日志为 `evidence/debug-startup-sdr-tonemap.log`。桌面自动化本轮遇到 `GetCursorPos` 拒绝访问，未取得界面操作与视觉证据；烟雾进程由测试清理，不把它当作正常关闭验证。

最终自包含目录也已启动并播放 HEVC 4K 样本到 EOF，日志为 `evidence/release-startup-hevc.log`；系统状态同样为 Windows HDR 未开启。`evidence/final-verification.json` 汇总两种配置各 138 个实际执行的测试、最终 DLL 哈希、许可证核验与人工待测范围。该启动检查不替代窗口交互、正常关闭或 HDR 屏幕验收。

## 固定测试素材

默认目录为仓库下 `artifacts/hdr-4k/media/`。三份素材均为 3840×2160、8 秒、无音轨。

| 文件 | 内容 | 用途 |
|---|---|---|
| `hevc-main10-hdr10-4k30.mkv` | HEVC Main 10、4:2:0 10-bit、30 fps、limited range、BT.2020 / PQ、HDR10 mastering display 与 content light SEI；图案横向滚动 | 检查实际 HEVC 解码路径、4K30 帧调度、HDR 开关和 tone mapping |
| `pq-gradient-10bit-4k.mkv` | FFV1 无损、4:2:0 10-bit、1 fps、BT.2020 / PQ；每帧逐像素一致 | 检查 10-bit 渐变、暗部和亮度阶梯；此文件使用软件解码，不作为硬解样本 |
| `av1-main10-sdr-4k30.mkv` | AV1 Main、4:2:0 10-bit、30 fps、BT.709 SDR 动态测试图 | 支持 AV1 硬解时记录实际硬解；不支持时检查明确的软件回退与可见错误/卡顿情况 |

HDR 图案由 `build/testing/HdrTestPattern.cs` 直接按 ST 2084 OETF 写入 10-bit 整数码值。上半部为 0–1000 nits 的等 PQ 码值横向渐变，共 660 个不同 Y′ 值；中间窄带为 0–5 nits 暗部渐变；下部从左到右的参考块为：

`0、0.005、0.05、0.5、5、50、100、203、400、600、1000 nits`。

这些数值是内容目标亮度，受 10-bit 量化、显示器峰值、Windows 设置和实际 tone mapping 影响，不是显示器测光结果。HEVC 元数据采用 BT.2020 原色、D65、1000 nits mastering maximum，MaxCLL/MaxFALL 都使用 1000 nits 上界。FFV1 渐变单独保留每个原始码值；自动校验会把解码后的第一帧与原始 YUV 的 SHA-256 比较，防止测试源本身退化为 8-bit。

合成图案适合找出通路、位深、渐变、错误恢复和基础帧调度问题，压缩复杂度低，不能代替真实高码率 4K 影片的长时间性能测试。

## 工具来源与再生成

FFmpeg 官方只发布源码；本轮使用其[官方下载页](https://ffmpeg.org/download.html)列出的 [Gyan Windows 构建](https://www.gyan.dev/ffmpeg/builds/)，固定为 `8.1.2-essentials_build`。它只用于开发机生成素材，不随播放器发布，也不替换项目锁定的原生依赖。

| 项目 | 固定值 |
|---|---|
| 下载地址 | `https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.7z` |
| 站方 SHA-256 | `E25B682664025D49034C981AFB4BAE36238A40F29A3CC1C713AD9A8B5B3528F6` |
| 校验来源 | [站方校验文件](https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.7z.sha256) |
| 实际本机工具目录 | `artifacts/hdr-4k/tools/ffmpeg-8.1.2-essentials_build/bin/` |

本机已下载、核验并解压固定 7z 包。工具完整版本、FFmpeg/FFprobe 可执行文件哈希、每份样本哈希、实际编码参数和图案数值写入 `media-manifest.json`；完整流元数据和首帧 SEI 另存为同目录 JSON。

在仓库根目录使用 PowerShell 7：

```powershell
.\build\testing\prepare-hdr-4k-media.ps1
.\build\testing\verify-hdr-4k-media.ps1 -Decode
```

已有清单时生成器只进行校验；需要重新编码时加 `-Regenerate`。可以用 `-FfmpegPath` 指定其他已安装的 FFmpeg，但必须含 `libx265`、`libaom-av1` 和 `ffv1` 编码器，脚本会记录实际版本及新的样本哈希。不同工具版本或 CPU 的有损编码结果可能不同，不把可再生成误称为跨机器字节完全一致。

脚本只进行 CPU 编码、元数据核对和可选 CPU 完整解码，不启动播放器，也不请求 GPU。校验内容包含 SHA-256、codec/profile、3840×2160、10-bit、时长、帧率、色彩标记、HEVC HDR10 SEI，以及 FFV1 渐变像素一致性。输出结果为 `media-verification.json`。

2026-09-11 本机已生成并通过三份素材的 SHA-256、完整元数据和全片 CPU 解码。无损渐变的第一帧与原始 YUV 哈希完全一致，上部渐变保留 660 个不同 Y′ 码值。仅检查元数据时另写 `media-metadata-verification.json`，保留完整解码记录。

| 本次生成文件 | 字节数 | SHA-256 |
|---|---:|---|
| `hevc-main10-hdr10-4k30.mkv` | 196211 | `E6C53385F6D49EC8A76861D109A1084545656EA2EFC897E7CC07E9716E815B65` |
| `pq-gradient-10bit-4k.mkv` | 1044030 | `BFBEBB8C19086D3DF9155192DC4B18E43E886C80350BAAF707717998786A7876` |
| `av1-main10-sdr-4k30.mkv` | 19596055 | `83B88042044DC5FCCE8660827F90FAA58EFD6FE580F7F67B53D469AFF3C6C3F3` |

首轮校验发现仅传编码器色彩选项时，容器流信息中的 primaries/transfer 未保留，校验按预期失败。生成器已同时通过 `setparams` 标注真实源帧，随后重新编码；PQ 数学图案没有改变。保留该失败记录，不把单靠文件名或 SEI 推断的 HDR 标签作为完整元数据验证。

## 今晚建议的测试顺序

自动化像素读回与显示器验收分开记录：`HdrGradientIntegrationTests` 读取同一份暂停的无损 PQ 图案，在不做 tone mapping 的目标峰值下对照 11 档 PQ 码值，再检查 1000 nits 目标和 SDR 输出的黑位、单调性与高光范围；报告使用 `hdr-gradient-pixel-report.json`。R10G10B10A2 连续码与 FP16 大于 1/负值/细小阶差等基础测试验证 GPU 表面精度。这些结果能证明像素通路及配置行为，不能证明显示器已经输出正确的物理亮度，也不能替代今晚的 Windows HDR/跨屏观察。

本轮新发布包目录为 `artifacts/hdr-4k/win-x64/`，从该目录的 `MpvShell.App.exe` 启动。已完成 Release 自包含发布、四 DLL 哈希/x64/加载/API 2.5、3 次会话创建销毁及 35 份许可证原文哈希校验，报告为目录内的 `publish-verification.json`。发布目录含 .NET/WinUI 运行时，应整体保留；重新生成命令：

```powershell
.\build\testing\publish-mvp.ps1 -OutputDirectory (Join-Path (Get-Location) artifacts/hdr-4k/win-x64) -NoRestore
```

先记录 Windows 版本、GPU/驱动、显示器型号、连接方式、分辨率/刷新率、DPI、Windows HDR 开关和“SDR 内容亮度”设置；涉及多个屏幕时分别记录。

1. **SDR 与 AV1 回退**：在 Windows HDR 关闭时打开 AV1 文件。核对实际解码器和实际 `hwdec` 状态，观察播放、暂停、seek、EOF 和重新播放。支持硬解的设备应有实际硬解证据；不支持时正常进入软件解码。选择了自动硬解不等于实际启用了硬解。
2. **HDR 文件转 SDR**：保持 Windows HDR 关闭，打开 HEVC HDR10 文件。信息中媒体仍应是 BT.2020/PQ，而显示器当前状态和输出应符合 SDR。观察画面没有整体灰白、明显黑位抬升或异常色彩，亮部经 tone mapping 显示。
3. **启用 HDR**：在支持 HDR 的显示器上打开 Windows HDR，重新播放同一 HEVC 文件。核对 Windows 实际 HDR 状态、应用输出模式、源位深与实际硬解路径；不能只根据文件名、媒体 PQ 标签或“10-bit”判定输出为 HDR。观察高亮度阶梯、暗部与视频上方 XAML 控件的可读性。
4. **10-bit 渐变**：打开无损 FFV1 渐变并暂停。上方渐变应连续，暗部块不应整体挤成一片；记录可见色带或抖动。根据显示器能力与同版本 mpv 对照，不能单凭普通截图证明 HDR 色彩精度。
5. **亮度设置与窗口切换**：调节 Windows 的 SDR 内容亮度，切换窗口/最大化/全屏，进行连续 resize。观察应用刷新显示器状态后视频与 UI 亮度仍合理，暂停画面稳定，无持续闪烁、黑屏或错位。
6. **HDR/SDR 跨屏**：如有两台不同模式的显示器，在播放与暂停状态下各移动窗口往返至少 5 次；检查输出名称、HDR 当前状态、亮度和渲染格式跟随改变。再在同一 HDR 显示器上切换 HDR 开关至少 3 次，观察能否恢复播放。没有对应硬件时如实记“未验证”。
7. **退出与性能记录**：对 HEVC/AV1 各循环播放、跳转和切换媒体至少 10 次，记录丢帧计数、CPU/GPU/显存、首帧时间、Present 失败、异常日志，再关闭应用。8 秒图案只提供基础回归；长时间和高码率性能要另加固定素材。

## 验收填写表

| 项目 | 预期证据 | 本轮结果 |
|---|---|---|
| 样本完整性与元数据 | `media-verification.json`，三份样本哈希及 CPU 解码 | 2026-09-11 通过；FFV1 首帧像素与原图一致 |
| Debug/Release 构建与自动化 | 完整命令、退出码、测试数 | 两种配置均 0 警告/错误，138/138 通过，无跳过 |
| HEVC Main10 4K 实际硬解 | decoder / hwdec 实际值、像素格式、无 CPU 逐帧复制的路径日志 | GTX 1060 实测通过，D3D11VA / d3d11-egl / P010，60 帧与非黑像素核验 |
| AV1 4K 硬解或软件回退 | 当前硬件能力与实际 decoder / hwdec 对照 | 当前设备实测软件回退通过，60 帧；AV1 硬解硬件覆盖未验证 |
| Windows HDR 开/关 | 系统状态、输出模式、相同素材观察 | 待人工验证 |
| 渐变、暗部、高光、XAML 亮度 | 显示器条件、对照配置、观察说明 | 待人工验证 |
| HDR/SDR 跨屏与全屏/DPI | 每次切换前后的显示器和输出日志 | 待人工验证 |
| 性能与关闭/恢复 | 丢帧、Present、内存、错误与释放顺序日志 | 待人工验证 |

人工测试发现问题时，保留“哪份素材、哪个时间点、窗口状态、显示器与 HDR 开关、实际解码/输出状态、复现步骤、日志路径”。私有视频和画面不纳入 Git。只有要求的真实硬件证据齐备后，才能更新 Gate D 结论。

编码参数参考：[x265 HDR10/master-display/max-cll](https://x265.readthedocs.io/en/master/cli.html)、[FFmpeg libaom AV1](https://ffmpeg.org/ffmpeg-codecs.html#libaom_002dav1)。显示器状态来源参考：[Windows DisplayInformation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.graphics.display.displayinformation)。
