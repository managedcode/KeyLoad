using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsOwner
{
    internal static DatabaseEngine ReadEngine(TestDatabase fixture, DatabaseLimits limits)
    {
        var physical = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)!.DefaultShard);
        var factory = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(limits),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null,
            fixture.MovementCheckpoints, fixture.Database.EvaluationClock);
        return factory(fixture.Store, physical);
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
        var operation = db.Database.CreateNativeOperation(OperationKind.Batch, id, D.Root,
            db.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(id, db.Partition,
                [new UpsertAnalyticsEvents(store, [.. events], revisions)])), work);
        var expected = new CommitReceipt(id, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId,
            checked(db.ReadJournalCut().LastIndex + 1), 1),
            [new(P.UpsertKind, store, events.OrderBy(row => row.Id, StringComparer.Ordinal).Last().Id, generation)], DurabilityProfile.ProcessDurable);
        var actual = db.SubmitIssued(operation).Get<CommitReceipt>();
        await O.EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }
}
