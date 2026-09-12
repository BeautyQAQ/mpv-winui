# TS 跳转优化：底层 seek 后视频流从关键帧起读（2026-09-12）

| 字段 | 内容 |
|---|---|
| 日期 / 时间 | 2026-09-12 12:40 至 14:10（北京时间） |
| 工作包 / Gate | Phase 0 后续优化（Gate A/D 相关，不改变闸口结论） |
| 环境 | 开发机 Windows 11 10.0.26200，GTX 1060 5GB（驱动 32.0.15.8180），.NET SDK 10.0.401；libmpv 由锁定源码增量重编（Clang/LLD 23、Meson 1.9.2） |
| 操作 | 阅读 mpv 0.41 `demux.c`/`demux_lavf.c`、FFmpeg `seek.c`/`mpegts.c`/`hevcdec.c` 定位根因；新增 `mpv-demux-seek-skip-to-keyframe.patch` 与 `--demuxer-skip-to-keyframe` 选项；应用固定开启；用同一文件、同一组目标对照四个变体；应用内暂停/播放恢复场景；Debug/Release 全量回归；闭包烟雾 |
| 结论 | **通过。** 产品配置下 TS 跳转后的 HEVC 参考帧错误从 430 组降为 0，落点误差不超过 11 ms，跳转延迟中位数增加约 14 ms；关闭补丁的对照变体错误数与改动前一致，证明修复来自补丁本身 |

## 根因

libavformat 的 MPEG-TS 没有关键帧索引，`av_seek_frame` 用时间戳二分（`mpegts_get_dts`）落在 GOP 中间。mpv 0.41 的 `demux.c` 只在**缓存内** seek 找不到目标包时才设置 `skip_to_keyframe`（`execute_cache_seek`），对新鲜的 demuxer seek 不设置，因此 `add_packet_locked` 会把落点之后的第一批非关键帧直接交给解码器。FFmpeg HEVC 在 `hevc_frame_start` 构建参考集失败，记录 `Could not find ref with POC` / `Error constructing the frame RPS` / `Skipping invalid undecodable NALU`，直到下一个 IRAP。这与硬解、软解无关（错误位于 hwaccel `start_frame` 之前），与 2026-09-12 上午的三方对照结论一致。

## 修复

补丁在 `queue_seek()` 里，对已经出现过关键帧的选中视频流设置 `skip_to_keyframe`，复用 `add_packet_locked()` 现有的"关键帧前丢包"逻辑；4096 包预算防止永不产生关键帧的流饿死（超预算时记一条 warn 并放行）；反向播放（`SEEK_SATAN`）与轨道切换的刷新 seek（直接设置 `in->seeking`，不经 `queue_seek`）不受影响。应用固定 `demuxer-skip-to-keyframe=yes` 并保留 `hr-seek-demuxer-offset=1`：回退窗口保证目标之前存在关键帧，hr-seek 仍精确到达目标帧。

## 对照结果

合成样片 `hevc-opengop-seek-720p60.ts`（`build/testing/prepare-ts-seek-media.ps1` 生成：1280×720@60，GOP 60 帧开放 GOP，B 帧 4，30 秒，SHA-256 `F32924CC…`），8 个目标：

| 变体 | 跳转错误组 | 有错误的跳转 | 落点最大偏差 | 延迟中位数 | VO 丢帧 |
|---|---:|---:|---:|---:|---:|
| 改动前 libmpv，硬解，offset=1 | 88 | 8 / 8 | 5 ms | 15.7 ms | 0 |
| **补丁，硬解，offset=1（产品配置）** | **0** | **0 / 8** | 5 ms | 15.4 ms | 0 |
| 补丁，软解，offset=1 | 0 | 0 / 8 | 5 ms | 47.3 ms | 0 |
| 补丁，硬解，offset=1，选项关闭（对照） | 88 | 8 / 8 | 5 ms | 15.3 ms | 0 |
| 补丁，硬解，offset=0 | 0 | 0 / 8 | 921 ms | 15.4 ms | 0 |

LG `Rays of Light` 4K60 HDR TS（SHA-256 `0E9E323A…`），与 RTX 3070 相同的 17 个目标：

| 变体 | 跳转错误组 | 有错误的跳转 | 落点最大偏差 | 延迟中位数 | VO 丢帧 |
|---|---:|---:|---:|---:|---:|
| 改动前 libmpv，硬解，offset=1（上午记录） | 430 | 16 / 17 | 11 ms | 114 ms | 0 |
| **补丁，硬解，offset=1（产品配置）** | **0** | **0 / 17** | 11 ms | 124 ms | 2 |
| 补丁，软解，offset=1 | 0 | 0 / 17 | 11 ms | 1173 ms | 12 |
| 补丁，硬解，offset=1，选项关闭（对照） | 430 | 16 / 17 | 11 ms | 109 ms | 0 |
| 补丁，硬解，offset=0 | 0 | 0 / 17 | 995 ms | 31 ms | 0 |

offset=0 变体说明为什么保留回退窗口：没有窗口时落点之后的第一个关键帧晚于目标，hr-seek 只能从该关键帧开始显示，落点晚约一个 GOP。产品配置 VO 丢帧 2 为 GTX 1060 上 4K60 播放 20 秒加 17 次跳转期间的计数，改动前同配置为 0；差异在噪声范围内，RTX 3070 复测时再核对。

## 回归

- 应用内恢复（`test-app-recovery.ps1 -Mode paused,playing`，合成 TS）：暂停位置 15.0213 秒重建前后一致，像素差 0；播放场景通过。报告 `artifacts/app-recovery/20260912-053233-…/`。
- Debug 与 Release 全量：0 警告 / 0 错误，各 184 通过、4 项素材驱动测试按环境跳过（其中 TS 对照已如上单独执行）。
- 闭包烟雾：四 DLL 加载、Client API 2.5 通过；`dumpbin /dependents` 直接导入与 2026-09-11 完全一致。
- 新 `libmpv-2.dll` SHA-256 `5E9D2D0DDED0A30D6B41AEB324D5200F84696BC7D9039B460DD680FC2804C705`，32,123,904 字节。

## 外机复测

- RTX 3070 第四轮复测（2026-09-12 13:47，构建 `a56cffd`）：同一 LG TS 两次加载 24 次精确跳转，`Could not find ref` 0 条，呈现丢帧全程 0，落点偏差 ≤ 12 ms，seek 到重启中位数 68 ms；用户目视「基本上没什么卡顿」。分析：`artifacts/rtx3070-analysis/134810-6b6e50f0/分析报告.md`。
- MP4/MKV 等有索引容器本来就落在关键帧，本补丁对它们无影响（MP4 41 次 seek 前后均 0 错误）。
