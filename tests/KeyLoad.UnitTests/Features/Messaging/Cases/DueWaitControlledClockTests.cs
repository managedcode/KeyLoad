using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests;

/// <summary>AC-TIME-003: controlled native timers join real replica apply waits.</summary>
internal sealed class DueWaitControlledClockTests
{
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Fallback = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);

    [Test]
    public async Task AdvancingClockCompletesFallbackAndLeavesApplySignalUsable()
    {
        var clock = new ControlledReadClock(Epoch);
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        var scope = new ControlledDueWaitScope(TestContext.Current!.Execution.CancellationToken);
        await scope.RunAsync(async () =>
        {
            var token = scope.Token;
            var wait = scope.Track(RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0, Fallback, clock, token));
            await Assert.That(wait.IsCompleted).IsFalse();
            clock.MoveUtc(Epoch.AddYears(1));
            await Assert.That(wait.IsCompleted).IsFalse();
            clock.Advance(Fallback);
            await wait.WaitAsync(Guard, TimeProvider.System, token);
            await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(0L);
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
            var following = scope.Track(RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0, Fallback, clock, token));
            fixture.Commit(ReplicaAppliedPositionWaitTests.FirstDocument);
            await following.WaitAsync(Guard, TimeProvider.System, token);
            await fixture.AssertDocument(ReplicaAppliedPositionWaitTests.FirstDocument);
            await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(1L);
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
        });
    }

    [Test]
    public async Task ProviderDeadlineCancelsWaitAndJoinsEveryOwnedTimer()
    {
        var clock = new ControlledReadClock(Epoch);
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        fixture.ConfigureResource();
        var scope = new ControlledDueWaitScope(TestContext.Current!.Execution.CancellationToken);
        await scope.RunAsync(async () =>
        {
            var token = scope.Token;
            using var deadline = new CancellationTokenSource(Fallback, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
            var wait = scope.Track(RecurringDueWait.WaitForChangeOrFallbackAsync(fixture.Consensus, 0, Fallback + Fallback, clock, linked.Token));
            clock.Advance(Fallback);
            var error = await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => wait.WaitAsync(Guard, TimeProvider.System, token));
            await Assert.That(error!.CancellationToken.IsCancellationRequested).IsTrue();
            await Assert.That(fixture.Consensus.AppliedPosition).IsEqualTo(0L);
            deadline.Dispose();
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
            fixture.Commit(ReplicaAppliedPositionWaitTests.FirstDocument);
            await fixture.AssertDocument(ReplicaAppliedPositionWaitTests.FirstDocument);
        });
    }
}
