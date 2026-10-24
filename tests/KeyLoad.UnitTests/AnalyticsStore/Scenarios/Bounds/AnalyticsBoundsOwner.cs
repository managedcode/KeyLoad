using System.Collections.Immutable;
using D = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.UnitTests.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.UnitTests.AnalyticsStore;

internal static class AnalyticsBoundsOwner
{
    internal static DatabaseEngine ReadEngine(TestDatabase fixture, DatabaseLimits limits)
    {
        var factory = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.RuntimeClaimsExecution(), null, fixture.Database.EvaluationClock);
        return factory(fixture.Store);
    }

    internal static async Task SeedAsync(TestDatabase db, AnalyticsEventData[] events)
    {
        await O.InitializeAsync(db, false);
        await PutAsync(db, D.Store, events, 1);
        var definition = new ResourceDefinition(P.HealthyStore, ResourceKind.AnalyticsStore, db.Partition.TransactionDomainId)
        {
            AnalyticsSchema = new([D.Kind], [D.Value])
        };
        await O.EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        await PutAsync(db, P.HealthyStore, [D.Event(db, D.First, D.Alpha, 7)], 1);
    }

    internal static async Task PutAsync(TestDatabase db, string store, AnalyticsEventData[] events, long generation,
        long expectedRevision = 0)
    {
        var work = new ReadExecutionBudget(db.Database.OperationLimitsOptions, db.Database.EvaluationClock,
            TestContext.Current!.Execution.CancellationToken);
        var id = Guid.NewGuid();
        var revisions = events.ToImmutableDictionary(row => row.Id, _ => expectedRevision, StringComparer.Ordinal);
        var operation = db.Database.CreateCommand(OperationKind.Batch, id, D.Root,
            db.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(new CommandRequest(id, db.Partition,
                [new UpsertAnalyticsEvents(store, [.. events], revisions)])), work);
        var expected = new CommitReceipt(id, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId,
            checked(db.Database.LastApplied + 1)), [new(P.UpsertKind, store, events.OrderBy(row => row.Id, StringComparer.Ordinal).Last().Id, generation)], DurabilityProfile.ProcessDurable, 1);
        var actual = db.SubmitIssued(operation).Get<CommitReceipt>();
        await O.EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }
}
