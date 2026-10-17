using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.UnitTests;

internal sealed class NativeSubmissionJournalBudgetCancellationCases
{
    [Test]
    public async Task ActualReplicaBeforeCopyCancellationAfterConstructorPreservesOneOwnerAndNativeCut()
    {
        var failures = new List<Exception>();
        ReplicaAppendBoundaryFixture? fixture = null;
        CancellationTokenSource? linked = null;
        ServerFailureObserver.Observe(() =>
        {
            fixture = new();
            linked = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        }, failures);
        if (fixture is not null && linked is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RequireAsync(fixture, linked), failures);
        }
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => fixture?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireAsync(ReplicaAppendBoundaryFixture fixture, CancellationTokenSource linked)
    {
        var entry = Append(fixture, "shared-budget-cancellation-original");
        var before = NativeSubmissionJournalBudgetCut.Read(fixture);
        var budget = new C1SubmissionJournalInspectionReadBudget(linked.Token);
        using var inspection = C1SubmissionJournalInspectionDatabase.Open(fixture.Canonical.Store, budget, linked.Token);
        var database = inspection.Database;
        await Assert.That(linked.IsCancellationRequested).IsFalse();
        long actualBytes = 0;
        var returned = false;
        var canceled = Assert.ThrowsExactly<OperationCanceledException>(() => NativeSubmissionJournalRead.Run(
            fixture.Store, database, fixture.Configuration, entry.Index, entry.Term, linked.Token, bytes =>
            {
                actualBytes = checked(actualBytes + bytes);
                linked.Cancel();
                budget.Charge(bytes);
                returned = true;
            }));
        await Assert.That(canceled.CancellationToken).IsEqualTo(linked.Token);
        await Assert.That(actualBytes > 0 && actualBytes <= 8192).IsTrue();
        await Assert.That(returned).IsFalse();
        await before.RequireUnchangedAsync(fixture);
        await RequireFreshAsync(fixture, entry);
        await RequireFreshAsync(fixture, Append(fixture, "shared-budget-cancellation-following"));
    }

    private static async Task RequireFreshAsync(ReplicaAppendBoundaryFixture fixture, ReplicaEntry entry)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var budget = new C1SubmissionJournalInspectionReadBudget(token);
        using var inspection = C1SubmissionJournalInspectionDatabase.Open(fixture.Canonical.Store, budget, token);
        var database = inspection.Database;
        var original = NativeSubmissionJournalRead.Run(fixture.Store, database, fixture.Configuration,
            entry.Index, entry.Term, token, budget.Charge);
        await Assert.That(original.NodeId).IsEqualTo(fixture.Store.Identity.NodeId);
        await Assert.That(original.Incarnation).IsEqualTo(fixture.Configuration.Incarnation);
        await Assert.That(original.Position).IsEqualTo(fixture.Store.Position);
        await Assert.That(original.NativeHardState.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(fixture.Log.State))).IsTrue();
        await Assert.That(original.NativeEntry.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(entry))).IsTrue();
    }

    private static ReplicaEntry Append(ReplicaAppendBoundaryFixture fixture, string document)
    {
        var original = fixture.Create(document, checked(fixture.Log.State.LastIndex + 1));
        fixture.Log.Append([original]);
        fixture.Log.Commit(original.Index);
        return original;
    }
}
