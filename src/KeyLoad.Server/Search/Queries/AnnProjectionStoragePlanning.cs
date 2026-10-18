using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server;

/// <summary>Reads enrollment and publication only while borrowing the actual open ANN storage.</summary>
internal sealed class AnnProjectionStoragePlanning(DatabaseEngine database, INativeAnnMaintenance native)
{
    internal Task<AnnProjectionJobState?> PrepareAsync(ProjectionConsumerRef consumer, long generation,
        Guid nodeId, AnnProjectionJobState? current, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var work = database.ReadAnnProjectionWorkBound(consumer, generation, budget, binding);
        if (work.NodeId != nodeId || work.Placement.PhysicalShardId != database.Store.Identity.NodeId
            || work.Placement.Incarnation != database.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
        return current is null ? InitializeAsync(work, budget, binding, token)
            : NextAsync(current, work, budget, binding, token);
    }

    private async Task<AnnProjectionJobState?> InitializeAsync(AnnProjectionWork work, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        var authority = work.Consumer.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionJobProtocol.Invalid);
        if (work.Consumer.InitialAnnMaintenance is { } reserved)
        {
            if (!work.Consumer.InitialAnnMaintenanceSettled)
            {
                return null;
            }
            if (reserved.NodeId != work.NodeId || reserved.Placement.PhysicalShardId != work.Placement.PhysicalShardId
                || reserved.Placement.Incarnation != work.Placement.Incarnation)
            {
                var ownInitial = AnnProjectionJobValidation.Initial(work);
                return new(authority, ownInitial, ownInitial, null, null, null, work.ThroughSequence, null);
            }
            var enrollment = await native.ReadEnrollmentBoundAsync(work.Principal, reserved, budget, binding, token).ConfigureAwait(true);
            if (enrollment is null)
            {
                return null;
            }
            var adopted = new AnnProjectionJobState(authority, enrollment.InitialRequest, null, null,
                enrollment.LastResult, null, enrollment.LastResult.Source?.ThroughSequence
                    ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid), enrollment.LastRequest);
            AnnProjectionJobValidation.RequireCurrent(adopted, work);
            return adopted;
        }
        var initial = AnnProjectionJobValidation.Initial(work);
        return new(authority, initial, initial, null, null, null, work.ThroughSequence, null);
    }

    private async Task<AnnProjectionJobState?> NextAsync(AnnProjectionJobState original,
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
            return original;
        }
        var publication = await native.ReadPublishedBoundAsync(work.Principal, original.InitialRequest, budget, binding, token)
            .ConfigureAwait(true);
        if (publication.Source.ThroughSequence >= work.ThroughSequence)
        {
            return null;
        }
        var restore = original.InitialRequest with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        return original with
        {
            PendingRequest = restore,
            JobId = null,
            RequiredThroughSequence = work.ThroughSequence
        };
    }

}
