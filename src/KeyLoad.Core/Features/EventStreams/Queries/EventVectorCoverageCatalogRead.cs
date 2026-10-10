using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCoverageCatalogRead
{
    private const string InvalidOwner = "The current native event vector coverage owner is inconsistent.";

    internal static EventVectorCoverageCatalog Read(IKeyValueView boundedView,
        PhysicalShardRecord configuredOwner, PhysicalShardRecord configuredControl,
        EventVectorInventoryReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(boundedView);
        ArgumentNullException.ThrowIfNull(configuredOwner);
        ArgumentNullException.ThrowIfNull(configuredControl);
        ArgumentNullException.ThrowIfNull(budget);
        var catalogRow = EventVectorCoverageRows.Required(boundedView,
            PhysicalShardCatalogRecordSerialization.CatalogKey(), budget);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(boundedView);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var ownersRow = EventVectorCoverageRows.Required(boundedView,
            PhysicalOwnerDirectorySerialization.Key(), budget);
        var owners = PhysicalOwnerDirectorySerialization.Read(boundedView)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidOwner);
        if (catalog is null || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredControl)
            || !PhysicalOwnerEntryValidation.SameOwner(owners.ControlOwner, configuredControl)
            || !owners.Owners.Any(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, configuredOwner)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, InvalidOwner); }
        return new(catalog, owners, catalogRow, ownersRow);
    }
}
