using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>AC-CQ-034/038: configured deadlines reach real replica execution without changing ordered apply admission.</summary>
internal sealed class ReplicaExecutionOptionsTests
{
    private const int ReadDeadlineMilliseconds = 50;
    private const int OuterTimeoutSeconds = 2;
    private const int ApplyTimeoutSeconds = 15;
    private const int AppliedEntries = 70;
    private const int SingleAppendEntry = 1;
    private const long CurrentTerm = 1;
    private static readonly TimeSpan OuterTimeout = TimeSpan.FromSeconds(OuterTimeoutSeconds);

    /// <summary>A nondefault deadline cancels the real read barrier while it awaits an unattached native transport.</summary>
    [Test]
    public async Task ConfiguredReadDeadlineCancelsWaitingTransportWithoutCallerCancellation()
    {
        var settings = new ReplicaExecutionOptions { ReadBarrierTimeout = TimeSpan.FromMilliseconds(ReadDeadlineMilliseconds) };
        await using var fixture = new ReplicaLifecycleFixture(settings);
        var caller = TestContext.Current!.Execution.CancellationToken;
        var pending = fixture.Consensus.ReadBarrierAsync(caller);

        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(OuterTimeout, caller));

        await Assert.That(caller.IsCancellationRequested).IsFalse();
        await Assert.That(fixture.Consensus.TransportReady.IsCompleted).IsFalse();
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(0L);
    }

    /// <summary>The existing default apply batch remains valid with an independently admitted single-entry append budget.</summary>
    [Test]
    public async Task DefaultApplyBatchDrainsCommittedPrefixWithSingleEntryAppendBudget()
    {
        await using var fixture = new ReplicaLifecycleFixture(maximumAppendEntries: SingleAppendEntry);
        fixture.Log.SaveTermAndVote(CurrentTerm, fixture.Configuration.LocalId);
        for (var index = SingleAppendEntry; index <= AppliedEntries; index++)
        {
            fixture.Log.Append([new(index, CurrentTerm, null)]);
        }
        fixture.Materializer.Commit(AppliedEntries);

        await fixture.Materializer.WaitForApplyAsync(AppliedEntries, TestContext.Current!.Execution.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(ApplyTimeoutSeconds));

        await Assert.That(fixture.Database.LastApplied).IsEqualTo((long)AppliedEntries);
        await Assert.That(fixture.Log.State.CommittedIndex).IsEqualTo((long)AppliedEntries);
        await Assert.That(fixture.Configuration.MaxAppendEntries).IsEqualTo(SingleAppendEntry);
    }

    /// <summary>Invalid native options are rejected by the actual owner before canonical recovery or worker publication.</summary>
    [Test]
    public async Task InvalidDeadlineIsRejectedBeforeMaterializerRecovery()
    {
        await using var fixture = new ReplicaLifecycleFixture();
        var before = fixture.Log.State;
        var snapshots = new ReplicaSnapshotStore(fixture.Database.Store, fixture.Log,
            ReplicaExecutionTestOptions.Configuration(fixture.Configuration), ReplicaExecutionTestOptions.Execution());
        var invalid = Options.Create(new ReplicaExecutionOptions { CommandTimeout = TimeSpan.Zero });

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = new ReplicaMaterializer(fixture.Database, fixture.Log, snapshots, invalid));

        await Assert.That(fixture.Log.State).IsEqualTo(before);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(0L);
    }
}
