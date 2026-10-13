using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionConsumerIdentityFixture
{
    internal const string Root = "root";
    internal const string Source = "orders";
    internal const string Target = "derived";
    internal const string ConsumerName = "canonical-projection";
    internal const string ConsumerSpace = "projection-consumer";
    private const string ForeignPartition = "foreign-partition";
    internal const string MissingRow = "The native projection flow requires its original committed consumer.";
    internal const string Detail = "The committed projection consumer does not match its canonical identity.";

    internal static ReplicatedOperation Operation<T>(OperationKind kind, Guid id, T request)
        => new(id, kind, Root, default, global::System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));

    internal static async Task<ProjectionConsumerIdentityState> SeedAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(Source, ResourceKind.Collection);
        database.Configure(Target, ResourceKind.Collection);
        var consumer = new ProjectionConsumerRef(database.Partition, ConsumerName);
        var definition = new ProjectionConsumerDefinition(1L, [Source], ["putDocument"]);
        var configureId = Guid.NewGuid();
        var configure = Operation(OperationKind.ConfigureProjectionConsumer, configureId,
            new ConfigureProjectionConsumerRequest(configureId, consumer, definition));
        var configured = database.Database.ApplyEmbedded(configure, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(configured.Get<ProjectionConsumerInfo>(),
            new ProjectionConsumerInfo(consumer, definition, 0L, false));
        var seedId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var seed = Operation(OperationKind.Batch, seedId, new CommandRequest(seedId, database.Partition,
            [new PutDocument(Source, "a", "{\"n\":1}", 0L), new PutDocument(Source, "b", "{\"n\":2}", 0L)])) with
        { EvaluatedAt = time };
        token.ThrowIfCancellationRequested();
        var seeded = database.Database.Apply(seed);
        var receipt = seeded.Get<CommitReceipt>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(seed, receipt,
            new("putDocument", Source, "a", 1L), new("putDocument", Source, "b", 1L));
        var batch = database.Database.ReadProjectionBatch(Root, new(consumer));
        var completeId = Guid.NewGuid();
        var complete = Operation(OperationKind.CommitProjectionBatch, completeId,
            new CommitProjectionBatchRequest(completeId, consumer, batch.Token, [new PutDocument(Target, "effect", "{\"sourceCount\":2}", 0L)]));
        var againId = Guid.NewGuid();
        var again = Operation(OperationKind.ConfigureProjectionConsumer, againId,
            new ConfigureProjectionConsumerRequest(againId, consumer, definition));
        var key = KeySpace.Partition(ConsumerSpace, database.Partition, ConsumerName);
        var original = database.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingRow);
        return new(database.Partition, consumer, definition, configure, configured, seed, seeded, receipt,
            time, batch, complete, again, key, original);
    }

    internal static void Damage(TestDatabase database, ProjectionConsumerIdentityState state, bool foreignPartition)
    {
        var refKey = foreignPartition
            ? state.Consumer with { Partition = new("foreign-tenant", "foreign-db", "orders", ForeignPartition) }
            : state.Consumer with { Name = "foreign-consumer" };
        var damaged = new ProjectionConsumerInfo(refKey, state.Definition, 0L, false);
        database.Store.Commit((transaction, _) => { transaction.PutRecord(state.Key, damaged); return true; });
    }
}
