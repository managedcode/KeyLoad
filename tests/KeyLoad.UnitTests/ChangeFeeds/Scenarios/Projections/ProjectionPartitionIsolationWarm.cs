namespace KeyLoad.UnitTests.ChangeFeeds;

internal static class ProjectionPartitionIsolationWarm
{
    internal static async Task<ProjectionPartitionIsolationState> RunAsync(TestDatabase database, CancellationToken token)
    {
        var first = await ProjectionConsumerIdentityFixture.SeedAsync(database, token);
        var second = await ProjectionPartitionIsolationFixture.SiblingAsync(database, first, token);
        await Assert.That(first.Partition.AtomicPartitionId == second.Partition.AtomicPartitionId).IsFalse();
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, first);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, second);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(database.Database, database.Store, first.Configure, first.Configured, token);
        var outcomes = new List<ProjectionPartitionOutcome>
        { new(first.Seed, first.Seeded), new(second.Configure, second.Configured), new(second.Seed, second.Seeded) };
        await RefusalsAsync(database, first, second, outcomes, token);
        var releaseId = Guid.NewGuid();
        var release = ProjectionConsumerIdentityFixture.Operation(database.Database, OperationKind.ReleaseProjectionConsumer, releaseId,
            new ReleaseProjectionConsumerRequest(releaseId, first.Consumer, 1L));
        var keys = ProjectionPartitionIsolationFixture.OriginalKeys(second);
        var before = ProjectionPartitionIsolationFixture.SelectedBytes(database.Store, keys);
        var released = database.Database.ApplyEmbedded(release, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(released.Get<ProjectionConsumerInfo>(),
            new ProjectionConsumerInfo(first.Consumer, first.Definition, 0L, true));
        outcomes.Add(new(release, released));
        var purge = ProjectionPartitionIsolationFixture.Purge(database.Database, first.Partition, 2L);
        var purged = database.Database.ApplyEmbedded(purge, token);
        await ProjectionConsumerIdentityAssertions.ExactAsync(purged.Get<OutboxHead>(), new OutboxHead(2L, 3L, 0L, 0L));
        outcomes.Add(new(purge, purged));
        await ProjectionPartitionIsolationAssertions.OriginalBytesAsync(database.Store, keys, before);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, second);
        foreach (var item in outcomes)
        {
            await ProjectionConsumerIdentityAssertions.ReplayAsync(database.Database, database.Store, item.Operation, item.Result, token);
        }
        await ProjectionPartitionIsolationSettlement.RunAsync(database.Database, second, outcomes, token);
        var result = new ProjectionPartitionIsolationState(first, second, [.. outcomes],
            ProjectionConsumerIdentityAssertions.Image(database.Store), database.Store.Position);
        await ProjectionPartitionIsolationAssertions.VerifyAsync(database.Database, database.Store, result, token);
        return result;
    }

    private static async Task RefusalsAsync(TestDatabase database, ProjectionConsumerIdentityState first,
        ProjectionConsumerIdentityState second, List<ProjectionPartitionOutcome> outcomes, CancellationToken token)
    {
        var keys = ProjectionPartitionIsolationFixture.OriginalKeys(first).Concat(ProjectionPartitionIsolationFixture.OriginalKeys(second)).ToArray();
        var before = ProjectionPartitionIsolationFixture.SelectedBytes(database.Store, keys);
        var id = Guid.NewGuid();
        var wrong = ProjectionConsumerIdentityFixture.Operation(database.Database, OperationKind.CommitProjectionBatch, id,
            new CommitProjectionBatchRequest(id, second.Consumer, first.Batch.Token,
                [new PutDocument(ProjectionConsumerIdentityFixture.Target, "effect", "{\"sourceCount\":2}", 0L)]));
        var refused = database.Database.ApplyEmbedded(wrong, token);
        await ProjectionPartitionIsolationAssertions.FailureAsync(refused, ErrorCode.TokenInvalidated, ProjectionPartitionIsolationFixture.ScopeDetail);
        await ProjectionPartitionIsolationAssertions.OriginalBytesAsync(database.Store, keys, before);
        outcomes.Add(new(wrong, refused));
        var pinned = ProjectionPartitionIsolationFixture.Purge(database.Database, second.Partition, 2L);
        var pinFailure = database.Database.ApplyEmbedded(pinned, token);
        await ProjectionPartitionIsolationAssertions.FailureAsync(pinFailure, ErrorCode.Conflict, ProjectionPartitionIsolationFixture.PinDetail);
        await ProjectionPartitionIsolationAssertions.OriginalBytesAsync(database.Store, keys, before);
        outcomes.Add(new(pinned, pinFailure));
        await Assert.That(database.Database.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(second.Partition, ProjectionConsumerIdentityFixture.Target, "effect"), cancellationToken: token)).IsNull();
    }
}
