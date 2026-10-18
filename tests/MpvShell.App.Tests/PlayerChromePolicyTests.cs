using FluentAssertions;
using MpvShell.App.Services;

namespace MpvShell.App.Tests;

public sealed class PlayerChromePolicyTests
{
    private static PlayerChromeContext IdlePlayback => new(
        IsReady: true,
        IsWindowActive: true,
        IsPlaying: true,
        IsStopped: false,
        HasOverlay: false,
        HasActiveInteraction: false,
        HasTimelinePreview: false,
        HasTransientUi: false,
        IsPointerOverControls: false,
        HasKeyboardFocusWithinControls: false);

    [Fact]
    public void Active_playback_can_hide_controls_after_idle_timeout()
    {
        PlayerChromePolicy.CanAutoHide(IdlePlayback).Should().BeTrue();
    }

    public static TheoryData<PlayerChromeContext> AutoHideBlockedStates => new()
    {
        IdlePlayback with { IsReady = false },
        IdlePlayback with { IsWindowActive = false },
        IdlePlayback with { IsPlaying = false },
        IdlePlayback with { IsStopped = true },
        IdlePlayback with { HasOverlay = true },
        IdlePlayback with { HasActiveInteraction = true },
        IdlePlayback with { HasTimelinePreview = true },
        IdlePlayback with { HasTransientUi = true },
        IdlePlayback with { IsPointerOverControls = true },
        IdlePlayback with { HasKeyboardFocusWithinControls = true },
    };

    [Theory]
    [MemberData(nameof(AutoHideBlockedStates))]
    public void Playback_or_input_state_can_keep_controls_visible(PlayerChromeContext context)
    {
        PlayerChromePolicy.CanAutoHide(context).Should().BeFalse();
    }

    [Fact]
    public void Returning_from_deactivation_requires_fresh_idle_timeout_eligibility()
    {
        var deactivated = IdlePlayback with { IsWindowActive = false };
        PlayerChromePolicy.CanAutoHide(deactivated).Should().BeFalse();
        PlayerChromePolicy.CanAutoHide(deactivated with { IsWindowActive = true }).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false, true, true, true, PlayerEscapeAction.CancelInteraction)]
    [InlineData(false, true, true, true, true, PlayerEscapeAction.CancelInteraction)]
    [InlineData(false, false, true, true, true, PlayerEscapeAction.CloseTransientUi)]
    [InlineData(false, false, false, true, true, PlayerEscapeAction.CloseOverlay)]
    [InlineData(false, false, false, false, true, PlayerEscapeAction.ExitFullscreen)]
    [InlineData(false, false, false, false, false, PlayerEscapeAction.None)]
    public void Escape_dismisses_one_layer_before_exiting_fullscreen(
        bool hasInteraction, bool hasPreview, bool hasTransientUi, bool hasOverlay, bool isFullscreen,
        PlayerEscapeAction expected)
    {
        PlayerChromePolicy.ResolveEscape(hasInteraction, hasPreview, hasTransientUi, hasOverlay, isFullscreen)
            .Should().Be(expected);
    }
}
