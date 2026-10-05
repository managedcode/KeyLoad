using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardTestBootstrap
{
    private static readonly Guid ShardId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");

    internal static void Bootstrap(DatabaseEngine database, string principalId)
    {
        if (database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)) is not null)
        {
            RequireExisting(database);
            return;
        }
        var request = new BootstrapPhysicalShardCatalogRequest(1, 0, ShardId, database.Store.Identity.Incarnation,
            PhysicalShardCatalogVoterIds.Standard);
        var operation = database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
            PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId), principalId,
            DateTimeOffset.UnixEpoch, NativeSerialization.Serialize(request));
        var result = database.Apply(operation);
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The unit test physical shard catalog did not bootstrap.");
        }
        RequireExisting(database);
    }

    internal static void RequireExisting(DatabaseEngine database)
    {
        var catalog = database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view));
        if (catalog is null || catalog.Version != 1 || catalog.Revision != 1
            || catalog.DefaultShard.PhysicalShardId != ShardId
            || catalog.DefaultShard.Incarnation != database.Store.Identity.Incarnation
            || catalog.DefaultShard.PlacementEpoch != 1
            || !catalog.DefaultShard.VoterIds.SequenceEqual(PhysicalShardCatalogVoterIds.Standard, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The persisted unit test physical shard catalog changed.");
        }
    }
}
