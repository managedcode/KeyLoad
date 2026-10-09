using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ClusterBackupAdministratorRequired = "Cluster capture requires an active persisted cluster administrator.";

    internal void AuthorizeClusterBackupOwner(IKeyValueView view, string principalId,
        ClusterBackupOwnerCapability capability, ReadExecutionBudget work)
    {
        work.Check();
        ClusterBackupRequestValidation.Require(capability.Request);
        var principal = RevalidateCredential(view, capability.Credential, principalId, Clock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterBackupAdministratorRequired); }
        if (Store.Identity.NodeId != capability.Request.ExpectedNodeId || configuredPhysicalOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(configuredPhysicalOwner, capability.Request.ExpectedOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ClusterBackupConfiguredOwnerRequired); }
        work.Check();
    }
}
