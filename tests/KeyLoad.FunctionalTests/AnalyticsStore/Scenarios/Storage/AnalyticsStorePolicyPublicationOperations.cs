using System.Collections.Immutable;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;
using S = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStorePolicyPublicationData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsStorePolicyPublicationOperations
{
    private const string PutKind = "putDocument";

    internal static async Task ConfigureAsync(TestDatabase db, long epoch, ImmutableArray<string> grants)
    {
        var expected = D.Principal(db, epoch, grants);
        var result = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected));
        await O.EqualAsync(expected, result.Get<PrincipalRecord>());
    }

    internal static async Task StaleAsync(TestDatabase db, AnalyticsAggregateSnapshot original)
    {
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ValidateAnalyticsAggregatePublication(original));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    internal static async Task UnrelatedAsync(TestDatabase db, AnalyticsAggregateSnapshot original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(S.OtherCollection, S.OtherId, S.OtherJson, ExpectedRevision: S.Absent)]);
        var expectedIndex = db.ReadCanonicalCut().AppliedPosition + S.Revision;
        var issued = db.Database.CreateCommand(OperationKind.Batch, command.CommandId, D.Root,
            db.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(command));
        var outcome = db.SubmitIssuedEmbedded(issued, token);
        await O.EqualAsync(new CommitReceipt(command.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, expectedIndex), [new(PutKind, S.OtherCollection, S.OtherId, S.Revision)], DurabilityProfile.ProcessDurable, 1),
            outcome.Get<CommitReceipt>());
        await Assert.That(db.Store.Position).IsGreaterThan(original.SourcePosition);
        var cut = AnalyticsStorePolicyPublicationCut.Capture(db);
        db.Database.ValidateAnalyticsAggregatePublication(original);
        await cut.AssertUnchangedAsync(db);
        var actual = db.Database.GetDocument(D.Root, new(db.Partition, S.OtherCollection, S.OtherId), cancellationToken: token);
        await O.EqualAsync(new DocumentResult(AnalyticsExpectedResourceCreation.Bind(db, new(db.Partition, S.OtherCollection, S.OtherId),
            AnalyticsExpectedResourceCreation.Unrelated),
            S.Revision, S.OtherJson, false, []), actual);
        await cut.AssertUnchangedAsync(db);
    }
}
