using FluentAssertions;

namespace MpvShell.Player.LibMpv.Tests;

public sealed class VideoRecoveryLeaseTests
{
    [Fact]
    public async Task Idle_recovery_should_block_playback_mutations_but_allow_color_configuration_and_reads()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 19, timeout.Token);
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var volume = session.SetPropertyAsync("volume", 37, timeout.Token);
            var mute = session.CommandAsync(["set", "mute", "yes"], timeout.Token);
            volume.IsCompleted.Should().BeFalse();
            mute.IsCompleted.Should().BeFalse();
            Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19);
            (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(false);

            await session.ConfigureVideoOutputAsync(MpvVideoOutputMode.Hdr10, 1000, timeout.Token);
            (await session.GetPropertyAsync("options/target-trc", timeout.Token)).Should().Be("pq");
            await session.ResumeVideoAfterRecoveryAsync(lease, timeout.Token);
            await Task.WhenAll(volume, mute);
            Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(37);
            (await session.GetPropertyAsync("mute", timeout.Token)).Should().Be(true);
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }
    }

    [Fact]
    public async Task Completing_old_lease_repeatedly_should_not_unlock_a_new_recovery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        var first = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        await session.ResumeVideoAfterRecoveryAsync(first, timeout.Token);
        await session.AbortVideoRecoveryAsync(first);
        var second = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            await session.AbortVideoRecoveryAsync(first);
            var mutation = session.SetPropertyAsync("mute", true, timeout.Token);
            mutation.IsCompleted.Should().BeFalse();
            var duplicateResume = () => session.ResumeVideoAfterRecoveryAsync(first, timeout.Token).AsTask();
            await duplicateResume.Should().ThrowAsync<InvalidOperationException>();
            mutation.IsCompleted.Should().BeFalse();
            await session.AbortVideoRecoveryAsync(second);
            await session.AbortVideoRecoveryAsync(second);
            await mutation;
        }
        finally { await session.AbortVideoRecoveryAsync(second); }
    }

    [Fact]
    public async Task Canceled_waiters_should_neither_mutate_playback_nor_release_the_active_recovery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var canceled = new CancellationTokenSource();
        await using var session = CreateSession();
        await session.InitializeAsync(timeout.Token);
        await session.SetPropertyAsync("volume", 19, timeout.Token);
        var lease = await session.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var mutation = session.SetPropertyAsync("volume", 99, canceled.Token);
            var suspension = session.SuspendVideoForRecoveryAsync(canceled.Token).AsTask();
            canceled.Cancel();
            Func<Task> waitMutation = async () => { await mutation; };
            Func<Task> waitSuspension = async () => { await suspension; };
            await waitMutation.Should().ThrowAsync<OperationCanceledException>();
            await waitSuspension.Should().ThrowAsync<OperationCanceledException>();
            var laterMutation = session.SetPropertyAsync("mute", true, timeout.Token);
            laterMutation.IsCompleted.Should().BeFalse();
            await session.AbortVideoRecoveryAsync(lease);
            await laterMutation;
            Convert.ToDouble(await session.GetPropertyAsync("volume", timeout.Token)).Should().Be(19);
        }
        finally { await session.AbortVideoRecoveryAsync(lease); }
    }

    [Fact]
    public async Task Another_session_cannot_consume_the_recovery_lease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var owner = CreateSession();
        await using var other = CreateSession();
        await owner.InitializeAsync(timeout.Token);
        var lease = await owner.SuspendVideoForRecoveryAsync(timeout.Token);
        try
        {
            var invalidResume = () => other.ResumeVideoAfterRecoveryAsync(lease, timeout.Token).AsTask();
            await invalidResume.Should().ThrowAsync<ArgumentException>();
            var invalidAbort = () => other.AbortVideoRecoveryAsync(lease).AsTask();
            await invalidAbort.Should().ThrowAsync<ArgumentException>();
            var mutation = owner.SetPropertyAsync("mute", true, timeout.Token);
            mutation.IsCompleted.Should().BeFalse();
            await owner.AbortVideoRecoveryAsync(lease);
            await mutation;
        }
        finally { await owner.AbortVideoRecoveryAsync(lease); }
    }

    private static MpvPlayerSession CreateSession() => new(new Dictionary<string, string> { ["ao"] = "null" });
}
