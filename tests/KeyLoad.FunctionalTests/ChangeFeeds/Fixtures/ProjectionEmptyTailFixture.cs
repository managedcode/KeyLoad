using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal static class ProjectionEmptyTailFixture
{
    internal static Command<DatabaseCommand> Operation<T>(global::KeyLoad.Core.DatabaseEngine owner,
        OperationKind kind, Guid id, T request, DateTimeOffset? evaluatedAt = null) where T : notnull
        => owner.CreateCommand(kind, id, ProjectionEmptyTailAssertions.Root,
            evaluatedAt ?? owner.EvaluationClock.GetUtcNow(), request);

    internal static Command<DatabaseCommand> Complete(global::KeyLoad.Core.DatabaseEngine owner,
        ProjectionConsumerRef consumer, string token, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return Operation(owner, OperationKind.CommitProjectionBatch, id, new CommitProjectionBatchRequest(id, consumer, token, [.. effects]));
    }

    internal static async Task<ProjectionEmptyTailState> WarmAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(ProjectionEmptyTailAssertions.Source, ResourceKind.Collection);
        database.Configure(ProjectionEmptyTailAssertions.Target, ResourceKind.Collection);
        var consumer = new ProjectionConsumerRef(database.Partition, ProjectionEmptyTailAssertions.ConsumerName);
        var definition = new ProjectionConsumerDefinition(1L, [ProjectionEmptyTailAssertions.Source], ["putDocument"]);
        var configureId = Guid.NewGuid();
        var configure = Operation(database.Database, OperationKind.ConfigureProjectionConsumer, configureId,
            new ConfigureProjectionConsumerRequest(configureId, consumer, definition));
        await ProjectionEmptyTailAssertions.ExactAsync(database.Database.ApplyEmbedded(configure, token).Get<ProjectionConsumerInfo>(),
            new ProjectionConsumerInfo(consumer, definition, 0L, false));
        var batch = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        await ProjectionEmptyTailAssertions.ExactAsync(batch, new ProjectionBatch(new(consumer, definition, 0L, false), [], 0L, batch.Token, false));
        var empty = Complete(database.Database, consumer, batch.Token);
        var emptyResult = database.Database.ApplyEmbedded(empty, token);
        await ProjectionEmptyTailAssertions.ResultAsync(empty, emptyResult, 0L);
        var sourceId = Guid.NewGuid();
        var mutation = new PutDocument(ProjectionEmptyTailAssertions.Source, "input", "{\"n\":1}", 0L);
        var source = Operation(database.Database, OperationKind.Batch, sourceId,
            new CommandRequest(sourceId, database.Partition, [mutation]), FeedAuthorityClock.Epoch);
        token.ThrowIfCancellationRequested();
        var sourceResult = database.Database.Apply(source);
        var sourceReceipt = sourceResult.Get<CommitReceipt>();
        await Assert.That(sourceReceipt.CommandId).IsEqualTo(sourceId);
        await ProjectionEmptyTailAssertions.ExactAsync(sourceReceipt.Mutations,
            global::System.Collections.Immutable.ImmutableArray.Create(new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Source, "input", 1L)));
        await ProjectionEmptyTailAssertions.ReplayAsync(database.Database, database.Store, empty, emptyResult, token);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 0L, 1L);
        var rejected = Complete(database.Database, consumer, batch.Token, new PutDocument(ProjectionEmptyTailAssertions.Target, "effect", "{\"sourceCount\":1}", 0L));
        var rejection = await ProjectionEmptyTailAssertions.RejectEffectsAsync(database.Database, database.Store, database.Partition, rejected, token);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 0L, 1L);
        await ProjectionEmptyTailAssertions.DocumentsAsync(database.Database, database, database.Partition, false);
        return await ContinueAsync(database, consumer, definition, empty, emptyResult, source, sourceResult, sourceReceipt, rejected, rejection, token);
    }

    private static async Task<ProjectionEmptyTailState> ContinueAsync(TestDatabase database, ProjectionConsumerRef consumer,
        ProjectionConsumerDefinition definition, Command<DatabaseCommand> empty, Result<object> emptyResult,
        Command<DatabaseCommand> source, Result<object> sourceResult, CommitReceipt sourceReceipt,
        Command<DatabaseCommand> rejected, Result<object> rejection, CancellationToken token)
    {
        var fresh = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        var mutation = new PutDocument(ProjectionEmptyTailAssertions.Source, "input", "{\"n\":1}", 0L);
        var entry = new OutboxEntry(1L, 0, sourceReceipt.Token, new DateTimeOffset(source.Timestamp), mutation,
            new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Source, "input", 1L), null,
            new(database.ExpectedReference(new(database.Partition, ProjectionEmptyTailAssertions.Source, "input")), 1L, mutation.Json, new RowAccess(), new DateTimeOffset(source.Timestamp)));
        await ProjectionEmptyTailAssertions.ExactAsync(fresh,
            new ProjectionBatch(new(consumer, definition, 0L, false), [entry], 1L, fresh.Token, false));
        var complete = Complete(database.Database, consumer, fresh.Token, new PutDocument(ProjectionEmptyTailAssertions.Target, "effect", "{\"sourceCount\":1}", 0L));
        var completion = database.Database.ApplyEmbedded(complete, token);
        await ProjectionEmptyTailAssertions.ResultAsync(complete, completion, 1L,
            new MutationReceipt("putDocument", ProjectionEmptyTailAssertions.Target, "effect", 1L));
        var filteredBatch = database.Database.ReadProjectionBatch(ProjectionEmptyTailAssertions.Root, new(consumer));
        await ProjectionEmptyTailAssertions.ExactAsync(filteredBatch,
            new ProjectionBatch(new(consumer, definition, 1L, false), [], 2L, filteredBatch.Token, false));
        var filtered = Complete(database.Database, consumer, filteredBatch.Token);
        var advancement = database.Database.ApplyEmbedded(filtered, token);
        await ProjectionEmptyTailAssertions.ResultAsync(filtered, advancement, 2L);
        await ProjectionEmptyTailAssertions.ConsumerAsync(database.Database, database.Store, consumer, definition, 2L, 2L);
        await ProjectionEmptyTailAssertions.DocumentsAsync(database.Database, database, database.Partition, true);
        return new(database, database.Partition, consumer, definition, empty, emptyResult, source, sourceResult, sourceReceipt,
            rejected, rejection, complete, completion, filtered, advancement, ProjectionEmptyTailAssertions.Image(database.Store), database.Store.Position);
    }
}
