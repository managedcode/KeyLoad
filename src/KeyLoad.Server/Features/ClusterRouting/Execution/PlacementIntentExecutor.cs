using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PlacementIntentExecutor(OrleansNode node, PartitionHost partition,
    PartitionMovementRuntime movement, PlacementIntentAdmissions admissions,
    PlacementCapacityCollector capacity, PlacementIntentPhaseExecution phases, TimeProvider clock)
    : IPlacementIntentExecutor
{
    public async Task<PlacementIntentV1> ApplyAsync(GrainRequestEnvelope trusted,
        ApplyPlacementIntentRequest request, ReadExecutionBudget work,
        Func<PlacementIntentV1, ValueTask> progress, CancellationToken token)
    {
        PlacementIntentV1? result = null;
        await movement.RunAsync(async current =>
        {
            var failures = new List<Exception>();
            PlacementIntentAdmissionLease? lease = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                lease = await admissions.EnterAsync(current).ConfigureAwait(false);
                result = await ApplyOwnedAsync(trusted, request, work, progress, current).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(() => lease?.Dispose(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, token).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed);
    }

    private async Task<PlacementIntentV1> ApplyOwnedAsync(GrainRequestEnvelope trusted,
        ApplyPlacementIntentRequest request, ReadExecutionBudget work,
        Func<PlacementIntentV1, ValueTask> progress, CancellationToken token)
    {
        var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, trusted, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var intent = partition.Database.ReadPlacementIntent(principal.Id, request.TenantId,
            request.DatabaseId, request.IntentId, work);
        if (request.ControlPartition != intent.Plan.ControlPartition || (intent.OriginalApplyRequest is { } retainedRequest
                ? retainedRequest != request : request.ExpectedRevision != intent.Revision)
            || trusted.CommandId == Guid.Empty
            || intent.OriginalApplyCommandId is { } originalApply && trusted.CommandId != originalApply)
        {
            throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
        }
        if (intent.Status == PlacementIntentStatus.Completed)
        {
            return intent;
        }
        intent = await ClaimAsync(trusted, principal.Id, intent, request, work, token).ConfigureAwait(false);
        while (intent.CompletedMoves < intent.Plan.Moves.Length)
        {
            token.ThrowIfCancellationRequested();
            work.Check();
            intent = await ApplyNextAsync(trusted, intent, work, token).ConfigureAwait(false);
            await progress(intent).ConfigureAwait(false);
            if (intent.Status == PlacementIntentStatus.Completed)
            {
                break;
            }
            intent = await ClaimAsync(trusted, principal.Id, intent, request, work, token).ConfigureAwait(false);
        }
        return intent;
    }
}
