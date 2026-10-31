using System.Collections.Immutable;
using System.Text.Json.Nodes;
using ManagedCode.Communication;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsStoreTestOperations
{
    internal static async Task InitializeAsync(TestDatabase db, bool privacy)
    {
        var definition = new ResourceDefinition(D.Store, ResourceKind.AnalyticsStore, db.Partition.TransactionDomainId)
        {
            AnalyticsSchema = new([D.Kind], [D.Value]),
            FieldPolicies = privacy ? [new(D.MetricPath, D.Classification, D.ReadGrant, D.UseGrant, D.WriteGrant)] : []
        };
        await EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        var anchor = new ResourceDefinition(D.AnchorCollection, ResourceKind.Collection, db.Partition.TransactionDomainId);
        await EqualAsync(anchor, AnalyticsExpectedResourceCreation.Configure(db, anchor, AnalyticsExpectedResourceCreation.Anchor));
        db.Commit(new PutDocument(D.AnchorCollection, D.AnchorId, D.AnchorJson, 0, Access: new(OwnerId: D.Owner)));
    }

    internal static async Task<AnalyticsStoreTestCommit> CommitAsync(TestDatabase db, Mutation mutation,
        string kind, string lastId, long generation, CancellationToken token)
    {
        var work = new ReadExecutionBudget(db.Database.EvaluationClock, token);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, db.Partition, [mutation]);
        var issued = db.Database.CreateCommand(OperationKind.Batch, id, D.Root,
            TimeProvider.System.GetUtcNow(), request, work);
        var next = checked(db.ReadCanonicalCut().AppliedPosition + 1);
        var result = db.SubmitIssuedEmbedded(issued, token);
        var expected = new CommitReceipt(id, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, next), [new(kind, D.Store, lastId, generation)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(expected, result.Get<CommitReceipt>());
        await Assert.That(result.Get<CommitReceipt>().Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        return new(issued, expected);
    }

    internal static async Task RefusedAsync(TestDatabase db, Mutation mutation, CancellationToken token)
    {
        var before = db.Database.GetOutboxStatus(D.Root, db.Partition);
        var work = new ReadExecutionBudget(db.Database.EvaluationClock, token);
        var id = Guid.NewGuid();
        var issued = db.Database.CreateCommand(OperationKind.Batch, id, D.Root,
            TimeProvider.System.GetUtcNow(), new CommandRequest(id, db.Partition, [mutation]), work);
        var result = db.SubmitIssuedEmbedded(issued, token);
        await EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.RevisionConflict, "The expected analytics event revision does not match.")), result);
        await Assert.That(result.Value).IsNull();
        await EqualAsync(before, db.Database.GetOutboxStatus(D.Root, db.Partition));
    }

    internal static ImmutableDictionary<string, long> Revisions(params (string Id, long Revision)[] values)
        => values.ToImmutableDictionary(value => value.Id, value => value.Revision, StringComparer.Ordinal);

    internal static async Task RowsAsync(TestDatabase db, string principal, IReadOnlyList<AnalyticsEventData> expected,
        IReadOnlyList<long> revisions, bool hideMetrics, CancellationToken token)
    {
        var records = expected.Select((value, index) => new AnalyticsEventRecord(value.Id, value.Timestamp,
            value.Dimensions, hideMetrics ? ImmutableDictionary<string, double>.Empty : value.Metrics,
            value.RowAccess, [.. value.Links.Select(link => AnalyticsExpectedResourceCreation.Bind(db, link,
                AnalyticsExpectedResourceCreation.Anchor))], revisions[index])).ToImmutableArray();
        await EqualAsync(new AnalyticsEventsPage(records, false, null), db.Database.ReadAnalyticsEvents(principal, D.Read(db), token));
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(expected)),
            JsonNode.Parse(JsonDefaults.Serialize(actual)))).IsTrue();
}
