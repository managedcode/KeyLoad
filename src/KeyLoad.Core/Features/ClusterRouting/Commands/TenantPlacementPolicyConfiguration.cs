using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteConfigurePlacementPolicy(IAtomicTransaction transaction,
        PrincipalRecord principal, ConfigurePlacementPolicyRequest request)
    {
        RequirePlacementAdministrator(principal);
        TenantPlacementPolicyValidation.Require(request.Policy, ErrorCode.Validation);
        if (request.ExpectedRevision < TenantPlacementPolicyProtocol.EmptyRevision
            || request.ExpectedRevision == long.MaxValue
            || request.Policy.Revision != checked(request.ExpectedRevision + TenantPlacementPolicyProtocol.RevisionStep))
        { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        RequirePlacementControlPartition(transaction, request.ControlPartition, request.Policy.TenantId,
            request.Policy.DatabaseId, catalog.DefaultShard);
        foreach (var owner in request.Policy.OwnerWeights)
        { _ = ResolveRegisteredPlacementOwner(transaction, catalog.DefaultShard, owner.PhysicalShardId, ErrorCode.OwnershipLost); }
        var current = TenantPlacementPolicyStorage.ReadPolicy(transaction, request.Policy.TenantId,
            request.Policy.DatabaseId, Limits.MaxBatchBytes);
        if ((current?.Revision ?? TenantPlacementPolicyProtocol.EmptyRevision) != request.ExpectedRevision)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        TenantPlacementPolicyStorage.WritePolicy(transaction, request.Policy, Limits.MaxBatchBytes);
        return Result(request.Policy);
    }

    private static void RequirePlacementAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, TenantPlacementPolicyProtocol.AdministratorRequired); }
    }
}
