using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;

namespace KeyLoad.Server;

/// <summary>Reads enrollment and publication only while borrowing the actual open ANN storage.</summary>
internal sealed class AnnProjectionStoragePlanning(DatabaseEngine database, IAnnMaintenance execution)
{
    internal async Task<AnnProjectionJobState?> PrepareAsync(ProjectionConsumerRef consumer, long generation,
        Guid nodeId, AnnProjectionJobState? current, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var work = database.ReadAnnProjectionWorkBound(consumer, generation, budget, binding);
        if (work.NodeId != nodeId || work.Placement.PhysicalShardId != database.Store.Identity.NodeId
            || work.Placement.Incarnation != database.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
        return (await (current is null ? InitializeAsync(work, budget, binding, token)
            : NextAsync(current, work, budget, binding, token))).Value;
    }

    private async Task<global::ManagedCode.Communication.Result<AnnProjectionJobState?>> InitializeAsync(AnnProjectionWork work, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var authority = work.Consumer.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionJobProtocol.Invalid);
        if (work.Consumer.InitialAnnMaintenance is { } reserved)
        {
            if (!work.Consumer.InitialAnnMaintenanceSettled)
            {
                return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.FailNotFound(nameof(InitializeAsync));
            }
            if (reserved.NodeId != work.NodeId || reserved.Placement.PhysicalShardId != work.Placement.PhysicalShardId
                || reserved.Placement.Incarnation != work.Placement.Incarnation)
            {
                var ownInitial = AnnProjectionJobValidation.Initial(work);
                return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.Succeed((AnnProjectionJobState?)(new(authority, ownInitial, ownInitial, null, null, null, work.ThroughSequence, null)));
            }
            var enrollment = await execution.ReadEnrollmentBoundAsync(work.Principal, reserved, budget, binding, token).ConfigureAwait(true);
            if (enrollment is null)
            {
                return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.FailNotFound(nameof(InitializeAsync));
            }
            var adopted = new AnnProjectionJobState(authority, enrollment.InitialRequest, null, null,
                enrollment.LastResult, null, enrollment.LastResult.Source?.ThroughSequence
                    ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid), enrollment.LastRequest);
            AnnProjectionJobValidation.RequireCurrent(adopted, work);
            return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.Succeed(((AnnProjectionJobState?)(adopted)));
        }
        var initial = AnnProjectionJobValidation.Initial(work);
        return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.Succeed((AnnProjectionJobState?)(new(authority, initial, initial, null, null, null, work.ThroughSequence, null)));
    }

    private async Task<global::ManagedCode.Communication.Result<AnnProjectionJobState?>> NextAsync(AnnProjectionJobState original,
        AnnProjectionWork work, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        AnnProjectionJobValidation.RequireCurrent(original, work);
        if (original.BlockedError is { } blocked)
        {
            throw Errors.Fail(blocked, AnnProjectionJobProtocol.Unresolved);
        }
        if (original.PendingRequest is not null)
        {
            return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.Succeed(((AnnProjectionJobState?)(original)));
        }
        var publication = await execution.ReadPublishedBoundAsync(work.Principal, original.InitialRequest, budget, binding, token)
            .ConfigureAwait(true);
        if (publication.Source.ThroughSequence >= work.ThroughSequence)
        {
            return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.FailNotFound(nameof(NextAsync));
        }
        var restore = original.InitialRequest with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        return global::ManagedCode.Communication.Result<AnnProjectionJobState?>.Succeed(((AnnProjectionJobState?)(original with
        {
            PendingRequest = restore,
            JobId = null,
            RequiredThroughSequence = work.ThroughSequence
        })));
    }

}
