using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal TenantPlacementPolicyV1 ReadPlacementPolicy(string principalId, TenantPlacementScope scope,
        ReadExecutionBudget work)
        => Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            RequirePlacementAdministrator(principal);
            JsonData.Identifier(scope.TenantId);
            JsonData.Identifier(scope.DatabaseId);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(charged)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            RequirePlacementControlPartition(charged, scope.ControlPartition, scope.TenantId,
                scope.DatabaseId, catalog.DefaultShard);
            var result = TenantPlacementPolicyStorage.ReadPolicy(charged, scope.TenantId, scope.DatabaseId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            TenantPlacementPolicyValidation.Require(result, ErrorCode.Corruption);
            work.CheckResult(result);
            return result;
        });
}
