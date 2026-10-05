namespace KeyLoad.Server;

internal static class PhysicalShardCatalogFence
{
    internal const string NotReady = "The physical shard catalog is not ready for public admission.";
    internal const string Mismatch = "The committed physical shard catalog does not match this node's configured identity.";
    internal const string AdministratorRequired = "The configured startup credential is not a persisted cluster administrator.";

    internal static bool Matches(PhysicalShardCatalog catalog, Guid physicalShardId,
        Guid incarnation, IReadOnlyList<string> voterIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(voterIds);
        var shard = catalog.DefaultShard;
        return catalog.Version == PhysicalShardCatalogStartupProtocol.Version
            && catalog.Revision == PhysicalShardCatalogStartupProtocol.InitialRevision && shard is not null
            && shard.PhysicalShardId == physicalShardId && shard.Incarnation == incarnation
            && shard.PlacementEpoch == PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch
            && shard.VoterIds.SequenceEqual(voterIds, StringComparer.Ordinal);
    }
}
