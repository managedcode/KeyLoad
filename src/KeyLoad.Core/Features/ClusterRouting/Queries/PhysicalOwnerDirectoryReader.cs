using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads registered identities under fresh persisted control administrator authority.</summary>
    /// <param name="principalId">The server-authenticated persisted principal.</param>
    /// <returns>The complete bounded directory; it grants no remote user operation authority.</returns>
    public PhysicalOwnerDirectoryV1 ReadPhysicalOwnerDirectory(string principalId)
        => Store.Read(view =>
        {
            var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator)
            { throw Errors.Fail(ErrorCode.PermissionDenied, PhysicalOwnerDirectoryProtocol.AdministratorRequired); }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
                ?? throw Errors.Fail(ErrorCode.NotFound, PhysicalOwnerDirectoryProtocol.Missing);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            var result = PhysicalOwnerDirectorySerialization.Read(view)
                ?? throw Errors.Fail(ErrorCode.NotFound, PhysicalOwnerDirectoryProtocol.Missing);
            if (!PhysicalOwnerEntryValidation.SameOwner(result.ControlOwner, catalog.DefaultShard))
            { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.ControlMismatch); }
            return result;
        });
}
