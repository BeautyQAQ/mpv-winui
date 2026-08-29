// Copyright (c) MpvShell contributors.
// Licensed under the MIT License.

using FluentAssertions;

namespace MpvShell.Rendering.WinUI.Tests;

public sealed class ResizeCoalescerTests
{
    [Fact]
    public void First_call_should_always_trigger_resize()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080).Should().BeTrue();
    }

    [Fact]
    public void Same_size_should_be_coalesced()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080);
        coalescer.ShouldResize(1920, 1080).Should().BeFalse();
    }

    [Fact]
    public void Different_size_should_trigger_resize()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080);
        coalescer.ShouldResize(2560, 1440).Should().BeTrue();
    }

    [Fact]
    public void Different_width_and_same_height_should_trigger()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080);
        coalescer.ShouldResize(3840, 1080).Should().BeTrue();
    }

    [Fact]
    public void Same_width_and_different_height_should_trigger()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080);
        coalescer.ShouldResize(1920, 2160).Should().BeTrue();
    }

    [Fact]
    public void Reset_should_allow_repeat_same_size()
    {
        var coalescer = new ResizeCoalescer();
        coalescer.ShouldResize(1920, 1080);
        coalescer.Reset();
        coalescer.ShouldResize(1920, 1080).Should().BeTrue();
    }
}