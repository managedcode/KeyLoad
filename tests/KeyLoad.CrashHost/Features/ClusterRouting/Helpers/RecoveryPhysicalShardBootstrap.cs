using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.CrashHost;

/// <summary>Prepares only current-format positive recovery fixtures through the real native command path.</summary>
internal static class RecoveryPhysicalShardBootstrap
{
    private const string ShardIdShardIdInputText = "778899aa-bbcc-ddee-ff00-112233445566";

    private static readonly Guid ShardId = Guid.Parse(ShardIdShardIdInputText);

    /// <summary>Creates or validates the fixture's independently identified canonical catalog.</summary>
    /// <param name="database">Actual node-owned canonical database.</param>
    /// <param name="principalId">Already persisted test administrator.</param>
    /// <param name="voterIds">Exact ordered voter identities configured by this fixture.</param>
    internal static void Bootstrap(DatabaseEngine database, string principalId, ImmutableArray<string> voterIds)
    {
        const int VersionSingleItemCount = 1;
        const int ExpectedRevisionEmptyCount = 0;
        const string BootstrapMessageText = "The recovery fixture catalog did not bootstrap.";

        ArgumentNullException.ThrowIfNull(database);
        var catalog = ReadExisting(database, principalId);
        if (catalog is null)
        {
            var request = new BootstrapPhysicalShardCatalogRequest(VersionSingleItemCount, ExpectedRevisionEmptyCount, ShardId,
                database.Store.Identity.Incarnation, voterIds);
            var operation = database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
                PhysicalShardCatalogIdentity.CreateBootstrapCommandId(ShardId), principalId,
                DateTimeOffset.UnixEpoch, NativeSerialization.Serialize(request));
            if (!database.Apply(operation).Get<bool>())
            {
                throw new InvalidOperationException(BootstrapMessageText);
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
        const int EmptyVersion = 1;
        const int EmptyRevision = 1;
        const int EmptyPlacementEpoch = 1;
        const string RequireExistingMessageText = "The persisted recovery fixture catalog changed.";

        var shard = catalog.DefaultShard;
        if (catalog.Version != EmptyVersion || catalog.Revision != EmptyRevision || shard.PhysicalShardId != ShardId
            || shard.Incarnation != database.Store.Identity.Incarnation || shard.PlacementEpoch != EmptyPlacementEpoch
            || !shard.VoterIds.SequenceEqual(voterIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(RequireExistingMessageText);
        }
    }
}
