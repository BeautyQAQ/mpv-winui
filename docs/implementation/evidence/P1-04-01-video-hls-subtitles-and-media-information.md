# P1-04 验收：视频 HLS、字幕与媒体信息

- 日期：2026-10-18。
- 输入提交：`d7522cba3e06c75ead25c91f700d10d9d5717ee5`；结论对应其上的本轮工作区改动，文件哈希保存在 `artifacts/p1-04/source-hashes.json`。
- 范围：[Phase 1 计划 P1-04](../phase-1-plan.md#p1-04视频-hls字幕与媒体信息验收)；沿用现有播放器接口与正式渲染组件，不新增交互面板或自适应码率策略。
- 结果：计划范围内通过。新增固定媒体、四项真实渲染集成、三项后端信息回归、两项 ViewModel 轨道用例；修复关闭音轨后音频编码残留。P1-05 尚未执行，未重新发布。

## 1. 输入与可再生成清单

生成入口：`build/testing/prepare-p1-04-media.ps1`。仅使用本机 FFmpeg/FFprobe，不在测试中下载或编码；无用户媒体。脚本记录工具哈希、完整命令、输出哈希、FFprobe 流信息，复用时逐文件验哈希。

| 样本 | 内容与用途 |
|---|---|
| `hls/index.m3u8`、`part00.ts`～`part05.ts` | H.264 8-bit BT.709，320×180、24 fps、12 秒；AAC 单声道 48 kHz；六个两秒 MPEG-TS 分片，VOD、相对分片地址 |
| `multitrack.mkv` | 同源视频；音轨 1 为 AAC / `eng` / 440 Hz，音轨 2 为 PCM / `zho` / 880 Hz；字幕 1 为 SubRip / `eng`，字幕 2 为 ASS / `zho`；显式标题覆盖 UTF-8 中文 |
| `english.srt`、`styled.ass` | 项目自写的固定字幕，覆盖 0～12 秒；SRT 底部文本、ASS 顶部黄色文本；使用系统 Arial，不附带字体 |
| `audio-only.wav` | 4 秒、32 kHz PCM，无视频和字幕，用于验证视频、HDR、轨道信息清理 |
| `video-only.mkv` | 4 秒、160×90 SDR H.264，无音频和字幕，用于验证音频信息与轨道清理 |

工具：FFmpeg `8.1.2-essentials_build`；ffmpeg SHA-256 `1326DDE4C84FF1F96FE6B8916C5BED29E163E9B5DCCF995F6F3DB069D143EC5E`，ffprobe SHA-256 `B49CCC7C6547B141AD5A2F6EC69CC04323D7133D7704D70B331B904C63EECB07`。

先生成 `artifacts/p1-04/media/` 做探测与复现，再使用最终脚本独立生成 `artifacts/p1-04/media-final/` 并复跑复用校验。最终 `media-manifest.json` SHA-256 为 `B8ED0E000C4663A112E1E6548970EF4E0BAFAF7F8495F769B69C827392FF5FA9`；其中列出全部 12 个输入文件。脚本验证流数量、实际编码、视频尺寸/帧率和分片数量；不承诺不同工具版本或不同运行生成逐字节相同的 Matroska 容器。

## 2. 执行环境与观测方式

- Windows x64，.NET SDK 10.0.402；ANGLE 报告 NVIDIA GeForce GTX 1060 5GB，驱动 `32.0.15.8266`，Direct3D 11，非 WARP。
- 固定本机随机端口 HTTP 服务，仅返回已登记的 VOD 播放列表和分片；可模拟播放列表/分片 404，以及持有 `part04.ts` 响应直至测试放行。
- `MediaFrameProbe` 复用 `RenderWorker`、`MpvRenderContext`、`AngleContext`、`D3D11DeviceManager`、`CompositionSwapChain`。同一 core 由 `LibMpvBackend` 控制，真实 Render API 写入 SwapChain；仅测试主动请求时通过 staging texture 读回像素。
- 未创建 WinUI 窗口、未人工目视；这是正式组件链路的原生像素集成证据，不是 GUI 交互验收。音频使用 `ao=null`，检查真实轨道选择和解码器，不声称完成扬声器听感验收。
- 四个媒体场景写独立 JSON，包含清单哈希、场景、实际信息/像素差或缓冲事件；完整结果以 TRX 为准。原生事件记录中的错误仅保存消息，避免直接序列化异常对象的反射字段。

## 3. 计划验收与结果

| 计划条目 | 本轮证据与结果 |
|---|---|
| 视频 HLS 变化画面、暂停、跨分片 seek、EOF、错误后重载 | `Video_hls_should_render…`：同一媒体 0.5 秒之后与 1.2 秒之后采样出现明显变化（Debug 最终报告 13,141 个变化像素，门槛 1,000）；暂停定位 4.5 秒后间隔 200 ms 的位置差不超过 1 ms、变化像素少于 10；再定位 8.5 秒，画面变化超过 1,000，HTTP 确认访问 `part02.ts` / `part04.ts`。11.5 秒继续播放到 EOF；随后分别经历播放列表 404、全部分片 404，返回 Loading 错误，同一会话重载有效 HLS 恢复实际画面 |
| 多轨 ID、语言、选择状态与字幕真实呈现 | `Multitrack_selection…`：音轨与字幕各自 ID 1/2，语言 `eng`/`zho`，中文标题正确；音轨 2/1 切换后原生 `aid`、后端选中状态与 PCM/AAC 编码同步。暂停在 3 秒，依次 SRT→关闭→ASS→关闭；原生 `sid` 与状态一致。Debug 最终 SRT 底部 504 个变化像素、顶部 0；ASS 顶部 2,288、底部 0；关闭后均恢复基准帧，差异少于 10 个像素 |
| 换媒体清理；EOF 保留；未知与零值区分 | `Switching_media…`：HLS→多轨 MKV→纯音频 WAV→无音轨短视频，旧字幕/轨道不遗留，纯音频无分辨率/位深/HDR/视频编码，短视频无音频编码且尺寸更新为 160×90。HLS EOF 后保留视频编码、分辨率及色彩类型。`BackendMediaInformationTests` 验证 EOF 清空原生属性时保留快照、下次 StartFile 清空，并区分缓冲 0% / 未知、丢帧 0 / 未知 |
| 真实缓冲和信息更新、高频 UI 派发、日志脱敏 | `A_held_video_segment…`：持有第五个分片，原生 `paused-for-cache=true` 与后端 BufferingChanged(true) 一致；Debug 最终约 5.730 秒进入缓冲，放行后转为 false，8.5～9.2 秒再次确认变化画面。媒体信息来自原生事件。实际 debug 网络日志含原生组件日志，但不含测试 token 或 `?token=`。全量回归包含既有 `UiPlayerEventPumpTests` 的有界批量、单个挂起 UI 回调和终态保护，不重新声称人工 UI 性能验收 |
| 明确未覆盖类型 | 本记录第 6 节限定 VOD/编码/字幕/机器范围；未泛化为全部 HLS、字幕或 HDR 兼容 |

两种配置的四项媒体测试均实际执行，无新增媒体场景跳过。`PlayerViewModelTrackSelectionTests` 新增“关闭字幕”路由和音频/字幕重复 ID 的隔离检查，确认关字幕不取消同 ID 音轨。

## 4. 发现的问题、修复与失败历史

**产品缺口：主动关闭音频后保留旧编码。** 原生轨道列表显示视频仍选中、两条音轨均未选中，随后 `audio-codec-name=null`；后端为了保留 EOF 信息而忽略空值，快照仍显示 `aac`。真实复现保存在 `audio-off-repro.log` / `audio-off-repro-reports/`，两种属性送达顺序的托管回归在 `unit-before/` 均失败。

修复限于 `LibMpvBackend`：当前已加载媒体仍有选中的视频且音轨全部取消选择时，清空音频编码并发布信息更新；EOF 清空整份轨道列表不触发此分支。因此不依赖 `track-list` 与 `codec=null` 的先后顺序。恢复音轨后仍由原生事件填回新编码。`fix-tests/` 的三项后端回归及四项真实媒体测试通过；最终两配置全量回归再次通过。

其余失败为验收实现校正，均保留原日志：

- `first-tests/`：两项通过，两项失败。将原生 `video-codec` 的展示文本误当为短编码名（实际 `H.264 / AVC / …`），以及误假定本地文件的缓冲属性必须为空。改为验证真实展示文本；本地 WAV 的原生 `cache-buffering-state` 实际返回 100，当前快照与其一致，不能将这个值误判为遗留网络缓存。
- `second-tests/`：三项通过，一项因直接查询不可用音频属性得到 mpv `-10` 而失败。改用原生 PropertyChange(null) 屏障，随后真实复现了上述产品缺口。
- `eof-trace-tests/`：验收报告直接序列化带异常的原生事件失败；改为保存异常消息，`eof-trace-fixed-tests/` 通过。未改变媒体判定条件。
- `log-check-tests/`：实际 debug 日志脱敏专项通过。未修改生产日志规则。

## 5. 最终命令与回归

PowerShell 7，仓库根目录执行；原始日志位于 `artifacts/p1-04/`：

```powershell
pwsh -File build/testing/prepare-p1-04-media.ps1 -OutputDirectory artifacts/p1-04/media-final
pwsh -File build/testing/prepare-p1-04-media.ps1 -OutputDirectory artifacts/p1-04/media-final
$env:MPVSHELL_TEST_P1_04_MEDIA_DIR = (Resolve-Path artifacts/p1-04/media-final).Path
foreach ($configuration in @('Debug','Release')) {
    $env:MPVSHELL_TEST_P1_04_REPORT_DIR = Join-Path (Get-Location) "artifacts/p1-04/$configuration-final-reports"
    dotnet build mpv-winui.slnx -p:Platform=x64 -c $configuration --no-restore
    dotnet test mpv-winui.slnx -p:Platform=x64 -c $configuration --no-build --no-restore --logger trx --results-directory "artifacts/p1-04/$configuration-final-tests"
}
```

| 配置 | 构建 | Abstractions | LibMpv | App | Rendering | 合计 |
|---|---|---|---|---|---|---|
| Debug x64 | 0 警告、0 错误 | 2 通过 | 126 通过 | 102 通过 | 72 通过、5 跳过 | 302 通过、0 失败、5 跳过 |
| Release x64 | 0 警告、0 错误 | 2 通过 | 126 通过 | 102 通过 | 72 通过、5 跳过 | 302 通过、0 失败、5 跳过 |

日志：`Debug-final-build.log`、`Release-final-build.log`、`Debug-final-tests.log`、`Release-final-tests.log`；TRX 在对应 `*-final-tests/`，每个配置四份；四个媒体场景报告在对应 `*-final-reports/`。退出码均为 0。

五项跳过为既有 HEVC 4K、AV1 4K、HDR 渐变、TS seek 对照和自然播放性能输入，未在本轮传入对应环境变量；不继承为本次通过。若不设置 P1-04 素材环境变量，新增四项也会明确跳过，本轮最终命令已显式设置并运行。

## 6. 限制与后续

- 覆盖本机 HTTP VOD、H.264/AAC MPEG-TS、Matroska 内嵌 SRT/ASS；未验收 master playlist / ABR、多变体音频组、直播/低延迟 HLS、fMP4、加密分片、网络认证，亦未验收外部字幕自动发现、WebVTT、PGS/VobSub、复杂 ASS 动画与字体附件。
- 本轮均为 SDR 小尺寸功能素材；不新增 HDR/4K/额外 GPU、跨屏、DPI 或真实触屏的支持声明，也未重跑 P1-03 GUI 场景。
- 这些测试包含人为停顿、seek、暂停和显式像素读回，不设吞吐/掉帧通过条件；例如 Debug 最终受控缓冲报告保留 127 次呈现掉帧。不得以四项通过宣称自然播放性能通过，PERF-02 仍独立待办。
- 生成了测试素材和本机构建输出，没有生成、登记或分发新发布包。当前唯一发布入口继续指向 10 月 2 日 GTX 1060 包。

**结论：P1-04 在声明范围内通过，MEDIA-01 关闭；下一工作包为 P1-05，Phase 1 尚未完成。**
