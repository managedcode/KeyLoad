using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string CatalogMissing = "The physical shard catalog is not initialized.";
    private const string AdministratorRequired = "Cluster administration is required.";

    /// <summary>Reads the canonical physical-shard catalog under current persisted administrator authority.</summary>
    /// <param name="principalId">Persisted principal whose authority is checked inside the store gate.</param>
    /// <returns>The committed catalog, or a typed not-found failure when bootstrap has not committed.</returns>
    public PhysicalShardCatalog ReadPhysicalShardCatalog(string principalId)
        => Store.Read(view => ReadPhysicalShardCatalog(view, principalId));

    internal PhysicalShardCatalog ReadPhysicalShardCatalog(IKeyValueView view, string principalId)
    {
        ArgumentNullException.ThrowIfNull(view);
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, AdministratorRequired);
        }

        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.NotFound, CatalogMissing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        return catalog;
    }

    internal PhysicalShardCatalog ReadPhysicalShardCatalog(string principalId, Guid physicalShardId,
        Guid incarnation, ImmutableArray<string> voterIds)
        => Store.Read(view => ReadPhysicalShardCatalog(view, principalId, physicalShardId, incarnation, voterIds));

    internal PhysicalShardCatalog ReadPhysicalShardCatalog(IKeyValueView view, string principalId,
        Guid physicalShardId, Guid incarnation, ImmutableArray<string> voterIds)
    {
        var catalog = ReadPhysicalShardCatalog(view, principalId);
        if (!PhysicalShardCatalogValidation.MatchesConfiguredHost(catalog.DefaultShard, physicalShardId,
            incarnation, voterIds))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogValidation.HostMismatch);
        }

        return catalog;
    }
}
