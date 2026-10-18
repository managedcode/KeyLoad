using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingEventVectorControlOwner = "The current event vector control owner is unavailable.";

    internal PhysicalShardRecord ReadEventVectorControlOwner(string principalId,
        EventFeedControlRequest request, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            if (request.ControlPartition is null || request.Scope is null)
            {
                throw Errors.Fail(ErrorCode.Validation, MissingEventVectorControlOwner);
            }
            ValidatePartition(request.ControlPartition);
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource,
                Capability.SubscriptionsManage);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, MissingEventVectorControlOwner);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            var placement = ResolveRegisteredPlacement(request.ControlPartition, catalog.DefaultShard);
            var owner = ResolveRegisteredPlacementOwner(bounded, catalog.DefaultShard,
                placement.PhysicalShardId, ErrorCode.OwnershipLost);
            cancellationToken.ThrowIfCancellationRequested();
            work.CheckResult(owner);
            return owner;
        });
    }
}
