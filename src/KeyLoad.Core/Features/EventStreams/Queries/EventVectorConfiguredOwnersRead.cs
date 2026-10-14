using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventVectorConfiguredOwners = "The current event vector configured owners are inconsistent.";

    internal PhysicalOwnerDirectoryV1 ReadEventVectorConfiguredOwners(string principalId,
        EventFeedControlRequest request, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        work.Check();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            if (request.ControlPartition is null || request.Scope is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorConfiguredOwners);
            }
            ValidatePartition(request.ControlPartition);
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource,
                Capability.SubscriptionsManage);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorConfiguredOwners);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            var placement = ResolveRegisteredPlacement(bounded, request.ControlPartition, catalog.DefaultShard);
            var owner = ResolveRegisteredPlacementOwner(bounded, catalog.DefaultShard,
                placement.PhysicalShardId, ErrorCode.OwnershipLost);
            var directory = PhysicalOwnerDirectorySerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorConfiguredOwners);
            if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, owner)
                || owner.Incarnation != Store.Identity.Incarnation)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorConfiguredOwners);
            }
            cancellationToken.ThrowIfCancellationRequested();
            work.CheckResult(directory);
            return directory;
        });
    }
}
