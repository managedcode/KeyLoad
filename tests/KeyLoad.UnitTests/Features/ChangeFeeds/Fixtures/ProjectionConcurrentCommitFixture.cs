using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionConcurrentCommitFixture
{
    internal static async Task<ProjectionConcurrentCommitState> WarmAsync(TestDatabase database,
        bool changed, CancellationToken token)
    {
        var source = await ProjectionConsumerIdentityFixture.SeedAsync(database, token);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, source);
        var first = Complete(source, ProjectionConcurrentCommitAssertions.Effect);
        var second = Complete(source, changed ? ProjectionConcurrentCommitAssertions.Alternative : ProjectionConcurrentCommitAssertions.Effect);
        var outcomes = await ProjectionConcurrentCommitRace.RunAsync(database.Database, first, second, token);
        var winner = await ProjectionConcurrentCommitAssertions.OutcomesAsync(first, outcomes.First, second, outcomes.Second, changed);
        await ProjectionConcurrentCommitAssertions.DocumentsAsync(database.Database, source, winner, changed, token);
        var batch = database.Database.ReadProjectionBatch(ProjectionConsumerIdentityFixture.Root, new(source.Consumer));
        await ProjectionConsumerIdentityAssertions.ExactAsync(batch,
            new ProjectionBatch(new(source.Consumer, source.Definition, 2L, false), [], 3L, batch.Token, false));
        var id = Guid.NewGuid();
        var filtered = ProjectionConsumerIdentityFixture.Operation(OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, source.Consumer, batch.Token, []));
        var advanced = database.Database.ApplyEmbedded(filtered, token);
        var result = advanced.Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(filtered, result.Receipt);
        await ProjectionConsumerIdentityAssertions.ExactAsync(result, new ProjectionBatchResult(result.Receipt, false, 3L));
        await ReplayAsync(database.Database, database.Store, source, first, outcomes.First, second, outcomes.Second, filtered, advanced, token);
        return new(source, first, outcomes.First, second, outcomes.Second, winner, filtered, advanced,
            ProjectionConsumerIdentityAssertions.Image(database.Store), database.Store.Position);
    }

    private static ReplicatedOperation Complete(ProjectionConsumerIdentityState source, string id)
    {
        var command = Guid.NewGuid();
        return ProjectionConsumerIdentityFixture.Operation(OperationKind.CommitProjectionBatch, command,
            new CommitProjectionBatchRequest(command, source.Consumer, source.Batch.Token,
                [new PutDocument(ProjectionConsumerIdentityFixture.Target, id, ProjectionConcurrentCommitAssertions.Json, 0L)]));
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, ProjectionConsumerIdentityState source,
        ReplicatedOperation first, OperationResult a, ReplicatedOperation second, OperationResult b,
        ReplicatedOperation filtered, OperationResult advanced, CancellationToken token)
    {
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source.Seed, source.Seeded, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, source.Configure, source.Configured, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, first, a, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, second, b, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, store, filtered, advanced, token);
    }
}
