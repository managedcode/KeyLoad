using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionCheckpointRetentionFixture
{
    internal static Command<DatabaseCommand> Purge(DatabaseEngine owner, PartitionRef partition, long through, int limit)
    {
        var id = Guid.NewGuid();
        return ProjectionConsumerIdentityFixture.Operation(owner, OperationKind.PurgeOutbox, id, new PurgeOutboxRequest(id, partition, through, limit));
    }

    internal static async Task<ProjectionCheckpointRetentionState> WarmAsync(TestDatabase database,
        bool ahead, CancellationToken token)
    {
        var source = await ProjectionConsumerIdentityFixture.SeedAsync(database, token);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, source);
        var pinned = Purge(database.Database, source.Partition, 2L, 2);
        var refused = database.Database.ApplyEmbedded(pinned, token);
        await ProjectionCheckpointRetentionAssertions.PinnedAsync(refused);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(database.Database, source);
        await ProjectionConsumerIdentityAssertions.SourcesAsync(database.Database, source);
        var probe = Purge(database.Database, source.Partition, 2L, 2);
        await Assert.That(probe.CommandId).IsNotEqualTo(pinned.CommandId);
        var state = OrleansSerialization.Deserialize<ProjectionConsumerInfo>(source.Original)
            ?? throw new InvalidOperationException(ProjectionConsumerIdentityFixture.MissingRow);
        database.Store.Commit((transaction, _) => { global::KeyLoad.Storage.StorageRecords.PutRecord(transaction, source.Key, state with { Checkpoint = ahead ? 3L : -1L }); return true; });
        var result = new ProjectionCheckpointRetentionState(source, pinned, refused, probe,
            ProjectionConsumerIdentityAssertions.Image(database.Store), database.Store.Position);
        await ProjectionCheckpointRetentionAssertions.CorruptAsync(database.Database, database.Store, result, token);
        return result;
    }

    internal static async Task RepairAsync(DatabaseEngine owner, ZoneTreeStore cold,
        ProjectionCheckpointRetentionState state, CancellationToken token)
    {
        await ProjectionConsumerIdentityAssertions.SameAsync(cold, state.DirtyImage, state.DirtyPosition);
        await ProjectionCheckpointRetentionAssertions.CorruptAsync(owner, cold, state, token);
        cold.Commit((transaction, _) => { transaction.Put(state.Source.Key, state.Source.Original); return true; });
        var prefix = Convert.ToHexString(state.Source.Key) + ":";
        var repaired = state.DirtyImage.Select(row => row.StartsWith(prefix, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(state.Source.Original) : row).ToArray();
        await ProjectionConsumerIdentityAssertions.SameAsync(cold, repaired, state.DirtyPosition + 1L);
        await ProjectionConsumerIdentityAssertions.OriginalBatchAsync(owner, state.Source);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Pinned, state.PinFailure, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Source.Seed, state.Source.Seeded, token);
        await ProjectionConsumerIdentityAssertions.ReplayAsync(owner, cold, state.Source.Configure, state.Source.Configured, token);
    }
}
