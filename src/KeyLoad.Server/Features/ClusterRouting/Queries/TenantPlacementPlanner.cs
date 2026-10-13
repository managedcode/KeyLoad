using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class TenantPlacementPlanner(PartitionHost partition,
    PartitionMovementRuntime movement, PlacementCapacityCollector collector, TimeProvider clock, IOptions<TenantPlacementExecutionOptions> options)
    : ITenantPlacementPlanner
{
    private const int FirstCapacityIndex = 0;
    public async Task<TenantPlacementPlanResultV1> PlanAsync(GrainRequestEnvelope trusted,
        TenantPlacementPlanRequest request, ReadExecutionBudget work, CancellationToken token)
    {
        TenantPlacementPlanResultV1? result = null;
        await movement.RunAsync(async current =>
        {
            var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, trusted, clock);
            GrainRequestAuthority.RequireAdministrator(principal);
            if (request.Partitions.IsDefaultOrEmpty || request.Partitions.Length > options.Value.MaximumPlanPartitions)
            { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
            foreach (var part in request.Partitions)
            { DatabaseEngine.ValidatePartition(part); }
            var capacity = await collector.ReadAsync(principal, request.Partitions, work,
                trusted.ExpiresAt, current).ConfigureAwait(false);
            var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() =>
            {
                result = partition.Database.CreateTenantPlacementPlan(principal.Id, request, capacity,
                    capacity[FirstCapacityIndex].CohortFingerprint, trusted.ExpiresAt, work, grant, current);
                return Task.CompletedTask;
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }, token).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed);
    }
}
