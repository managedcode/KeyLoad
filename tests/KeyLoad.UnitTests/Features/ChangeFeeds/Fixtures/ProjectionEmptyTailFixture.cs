namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionEmptyTailFixture
{
    internal static ReplicatedOperation Operation<T>(OperationKind kind, Guid id, T request)
        => new(id, kind, ProjectionEmptyTailAssertions.Root, default,
            global::System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));

    internal static ReplicatedOperation Complete(ProjectionConsumerRef consumer, string token, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return Operation(OperationKind.CommitProjectionBatch, id, new CommitProjectionBatchRequest(id, consumer, token, [.. effects]));
    }

    internal static async Task<ProjectionEmptyTailState> WarmAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(ProjectionEmptyTailAssertions.Source, ResourceKind.Collection);
        database.Configure(ProjectionEmptyTailAssertions.Target, ResourceKind.Collection);
        var consumer = new ProjectionConsumerRef(database.Partition, ProjectionEmptyTailAssertions.ConsumerName);
        var definition = new ProjectionConsumerDefinition(1L, [ProjectionEmptyTailAssertions.Source], ["putDocument"]);
        var configureId = Guid.NewGuid();
        var configure = Operation(OperationKind.ConfigureProjectionConsumer, configureId,
            new ConfigureProjectionConsumerRequest(configureId, consumer, definition));
        await ProjectionEmptyTailAssertions.ExactAsync(database.Database.ApplyEmbedded(configure, token).Get<ProjectionConsumerInfo>(),
            new ProjectionConsumerInfo(consumer, definition, 0L, false));
        var batch = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        await ProjectionEmptyTailAssertions.ExactAsync(batch, new ProjectionBatch(new(consumer, definition, 0L, false), [], 0L, batch.Token, false));
        var empty = Complete(consumer, batch.Token);
        var emptyResult = database.Database.ApplyEmbedded(empty, token);
        await ProjectionEmptyTailAssertions.ResultAsync(empty, emptyResult, 0L);
        var sourceId = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionEmptyTailAssertions.Source, "input", "{\"n\":1}", 0L);
        var source = Operation(OperationKind.Batch, sourceId, new CommandRequest(sourceId, database.Partition, [mutation]))
            with
        { EvaluatedAt = FeedAuthorityClock.Epoch };
        token.ThrowIfCancellationRequested();
        var sourceResult = database.Database.Apply(source);
        var sourceReceipt = sourceResult.Get<CommitReceipt>();
        await Assert.That(sourceReceipt.CommandId).IsEqualTo(sourceId);
        await ProjectionEmptyTailAssertions.ExactAsync(sourceReceipt.Mutations,
            global::System.Collections.Immutable.ImmutableArray.Create(new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Source, "input", 1L)));
        await ProjectionEmptyTailAssertions.ReplayAsync(database.Database, database.Store, empty, emptyResult, token);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 0L, 1L);
        var rejected = Complete(consumer, batch.Token, new PutDocument(ProjectionEmptyTailAssertions.Target, "effect", "{\"sourceCount\":1}", 0L));
        var rejection = await ProjectionEmptyTailAssertions.RejectEffectsAsync(database.Database, database.Store, database.Partition, rejected, token);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 0L, 1L);
        await ProjectionEmptyTailAssertions.DocumentsAsync(database.Database, database.Partition, false);
        return await ContinueAsync(database, consumer, definition, empty, emptyResult, source, sourceResult, sourceReceipt, rejected, rejection, token);
    }

    private static async Task<ProjectionEmptyTailState> ContinueAsync(TestDatabase database, ProjectionConsumerRef consumer,
        ProjectionConsumerDefinition definition, ReplicatedOperation empty, OperationResult emptyResult,
        ReplicatedOperation source, OperationResult sourceResult, CommitReceipt sourceReceipt,
        ReplicatedOperation rejected, OperationResult rejection, CancellationToken token)
    {
        var fresh = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        var mutation = new PutDocument(ProjectionEmptyTailAssertions.Source, "input", "{\"n\":1}", 0L);
        var entry = new OutboxEntry(1L, 0, sourceReceipt.Token, source.EvaluatedAt, mutation,
            new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Source, "input", 1L), null,
            new(new(database.Partition, ProjectionEmptyTailAssertions.Source, "input"), 1L, mutation.Json, new RowAccess(), source.EvaluatedAt));
        await ProjectionEmptyTailAssertions.ExactAsync(fresh,
            new ProjectionBatch(new(consumer, definition, 0L, false), [entry], 1L, fresh.Token, false));
        var complete = Complete(consumer, fresh.Token, new PutDocument(ProjectionEmptyTailAssertions.Target, "effect", "{\"sourceCount\":1}", 0L));
        var completion = database.Database.ApplyEmbedded(complete, token);
        await ProjectionEmptyTailAssertions.ResultAsync(complete, completion, 1L,
            new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Target, "effect", 1L));
        var filteredBatch = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        await ProjectionEmptyTailAssertions.ExactAsync(filteredBatch,
            new ProjectionBatch(new(consumer, definition, 1L, false), [], 2L, filteredBatch.Token, false));
        var filtered = Complete(consumer, filteredBatch.Token);
        var advancement = database.Database.ApplyEmbedded(filtered, token);
        await ProjectionEmptyTailAssertions.ResultAsync(filtered, advancement, 2L);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 2L, 2L);
        await ProjectionEmptyTailAssertions.DocumentsAsync(database.Database, database.Partition, true);
        return new(database.Partition, consumer, definition, empty, emptyResult, source, sourceResult, sourceReceipt,
            rejected, rejection, complete, completion, filtered, advancement, ProjectionEmptyTailAssertions.Image(database.Store), database.Store.Position);
    }
}
