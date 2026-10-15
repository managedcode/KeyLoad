using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeSeed
{
    internal static async Task<OperationalAnalyticsNativeCut> RecordAsync(OperationalAnalyticsNativeOwner owner,
        OperationalAnalyticsOwner analytics, CancellationToken token)
    {
        var db = owner.Canonical;
        token.ThrowIfCancellationRequested();
        db.Configure(P.Collection, ResourceKind.Collection);
        var first = Snapshot(db);
        await Assert.That(first.CommittedIndex).IsEqualTo(P.ConfigureCut);
        await Assert.That(first.MaterializedPosition).IsEqualTo(P.ConfigureCut);
        await Assert.That(analytics.TryRecord(first)).IsTrue();
        token.ThrowIfCancellationRequested();
        var receipt = db.Commit(new PutDocument(P.Collection, P.Document, P.Json, P.NoValue));
        var second = Snapshot(db);
        await Assert.That(second.CommittedIndex).IsEqualTo(P.DocumentCut);
        await Assert.That(second.MaterializedPosition).IsEqualTo(P.DocumentCut);
        await Assert.That(analytics.TryRecord(second)).IsTrue();
        // Fixed typed numeric ingestion control; not a fabricated observation from StateAsync.
        await Assert.That(analytics.TryRecord(second with
        {
            LastIndex = P.ArithmeticCommitted,
            CommittedIndex = P.ArithmeticCommitted,
            MaterializedPosition = P.ArithmeticApplied
        })).IsTrue();
        var original = db.ReadRetainedEntry(receipt.CommandId).Operation;
        ArgumentNullException.ThrowIfNull(original);
        var expected = new CommitReceipt(original.Id, new(db.Store.Identity.Incarnation,
            db.Partition.AtomicPartitionId, P.DocumentCut, P.OwnershipEpoch),
            [new(P.PutKind, P.Collection, P.Document, P.Revision)], DurabilityProfile.ProcessDurable);
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        return new(original, receipt, Rows(db), db.Store.Position, db.Store.Identity with
        {
            SigningKey = db.Store.Identity.SigningKey.ToArray()
        },
            db.ReadJournalCut(), second);
    }

    private static ReplicaNodeState Snapshot(TestDatabase db)
    {
        var cut = db.ReadJournalCut();
        return new(P.PrivateVoter, P.PrivateLeader, ReplicaRole.Follower, cut.Term, cut.LastIndex,
            cut.CommittedIndex, db.Database.LastApplied, P.NoSnapshot, false);
    }

    internal static string[] Rows(TestDatabase db)
    {
        var page = db.Store.Read(view => view.Scan([], P.MaximumRows));
        if (page.HasMore)
        {
            throw new InvalidOperationException(P.Invalid);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + P.RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
    }
}
