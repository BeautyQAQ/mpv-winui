using FluentAssertions;
using MpvShell.Player.LibMpv;

namespace MpvShell.Rendering.WinUI.Tests;

/// <summary>
/// 表面未绑定或输出重配期间，渲染器必须继续响应 mpv 的渲染请求。否则 vo_libmpv 的 flip_page
/// 每帧等待 200 ms 超时、记录“mpv_render_context_render() not being called or stuck”，
/// 并把该帧累计到 frame-drop-count（RTX 3070 日志中 HDR 切换约 0.6 秒卡顿与初段 8 帧丢帧的来源）。
/// </summary>
public sealed class RenderContinuityIntegrationTests
{
    [Fact]
    public async Task Renderer_should_keep_consuming_frames_without_vo_drops_while_it_cannot_present()
    {
        var path = Path.Combine(Path.GetTempPath(), "mpvshell-continuity-" + Guid.NewGuid().ToString("N") + ".y4m");
        NativeRenderIntegrationTests.WriteColorVideo(path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var session = new MpvPlayerSession(new Dictionary<string, string> { ["ao"] = "null" });
        await using var backend = new LibMpvBackend(session);
        try
        {
            await backend.InitializeAsync(cancellation.Token);
            await using var renderer = new D3D11VideoSurfaceRenderer();
            // 只初始化、不绑定 SwapChainPanel：这就是页面尚未附加或输出重配时“不能呈现”的状态。
            await renderer.InitializeAsync(session, cancellation.Token);
            await backend.LoadUrlAsync(path, cancellation.Token);
            await WaitForPositionAsync(session, 1.0, cancellation.Token);

            // 播放中切换 SDR → HDR10 → SDR 输出；重配期间 mpv 仍应按时序拿回每一帧。
            var hdrDisplay = new DisplayOutputCapabilities
            {
                DisplayName = "测试 HDR 显示器", IsAvailable = true, IsHdrSupported = true,
                ColorKind = DisplayOutputColorKind.HighDynamicRange, PeakLuminanceInNits = 1000,
            };
            var sdrDisplay = new DisplayOutputCapabilities
            {
                DisplayName = "测试 SDR 显示器", IsAvailable = true, ColorKind = DisplayOutputColorKind.StandardDynamicRange,
            };
            await renderer.UpdateOutputAsync(isHdrSource: true, hdrDisplay, cancellation.Token);
            await WaitForPositionAsync(session, 2.0, cancellation.Token);
            await renderer.UpdateOutputAsync(isHdrSource: true, sdrDisplay, cancellation.Token);
            await WaitForPositionAsync(session, 3.0, cancellation.Token);

            var drops = await session.GetPropertyAsync("frame-drop-count", cancellation.Token);
            drops.Should().Be(0L, "未呈现的帧必须以跳过绘制的方式及时消费，不能让 mpv 等待超时后丢弃");
            (await session.GetPropertyAsync("pause", cancellation.Token)).Should().Be(false);

            // 先停止媒体再释放渲染上下文，避免清理阶段产生 VO 重建错误日志。
            await session.CommandAsync(["stop"], cancellation.Token);
            while (await session.GetPropertyAsync("idle-active", cancellation.Token) is not true)
                await Task.Delay(20, cancellation.Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task WaitForPositionAsync(MpvPlayerSession session, double seconds, CancellationToken cancellationToken)
    {
        while (await session.GetPropertyAsync("time-pos", cancellationToken) is not double position || position < seconds)
            await Task.Delay(50, cancellationToken);
    }
}
