using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.CrashHost;

/// <summary>Prepares only current-format positive recovery fixtures through the real native command path.</summary>
internal static class RecoveryPhysicalShardBootstrap
{
    private static readonly Guid ShardId = Guid.Parse("778899aa-bbcc-ddee-ff00-112233445566");

    /// <summary>Creates or validates the fixture's independently identified canonical catalog.</summary>
    /// <param name="database">Actual node-owned canonical database.</param>
    /// <param name="principalId">Already persisted test administrator.</param>
    /// <param name="voterIds">Exact ordered voter identities configured by this fixture.</param>
    internal static void Bootstrap(DatabaseEngine database, string principalId, ImmutableArray<string> voterIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        var catalog = ReadExisting(database, principalId);
        if (catalog is null)
        {
            var request = new BootstrapPhysicalShardCatalogRequest(1, 0, ShardId,
                database.Store.Identity.Incarnation, voterIds);
            var operation = database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
                PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId), principalId,
                DateTimeOffset.UnixEpoch, NativeSerialization.Serialize(request));
            if (!database.Apply(operation).Get<bool>())
            {
                throw new InvalidOperationException("The recovery fixture catalog did not bootstrap.");
            }
            catalog = database.ReadPhysicalShardCatalog(principalId);
        }
        RequireExisting(database, catalog, voterIds);
    }

    private static PhysicalShardCatalog? ReadExisting(DatabaseEngine database, string principalId)
    {
        try
        {
            return database.ReadPhysicalShardCatalog(principalId);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.NotFound)
        {
            return null;
        }
    }

    private static void RequireExisting(DatabaseEngine database, PhysicalShardCatalog catalog,
        ImmutableArray<string> voterIds)
    {
        var shard = catalog.DefaultShard;
        if (catalog.Version != 1 || catalog.Revision != 1 || shard.PhysicalShardId != ShardId
            || shard.Incarnation != database.Store.Identity.Incarnation || shard.PlacementEpoch != 1
            || !shard.VoterIds.SequenceEqual(voterIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The persisted recovery fixture catalog changed.");
        }
    }
}
