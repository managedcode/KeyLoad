using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PlacementIntentPersistenceExecution(PartitionHost partition, PartitionMovementRuntime movement,
    PlacementCapacityCollector collector, TimeProvider clock, IOptions<DatabaseLimits> limits) : IPlacementIntentPersistence
{
    private const int FirstCapacityIndex = 0;
    public async Task<ReplicatedOperation> SealAsync(GrainRequestEnvelope trusted,
        PersistPlacementIntentRequest request, CancellationToken token)
    {
        ReplicatedOperation? result = null;
        await movement.RunAsync(async current =>
        {
            var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, trusted, clock);
            var work = new ReadExecutionBudget(limits, clock, current);
            work.ConstrainLifetime(trusted.ExpiresAt);
            var empty = ImmutableArray<OwnerCapacitySnapshotV1>.Empty;
            var prepared = partition.Database.CreateVerifiedPlacementIntentPersistence(trusted.CommandId,
                principal.Id, request, empty, null);
            if (!partition.Database.RequiresFreshPlacementPersistence(prepared, work))
            { result = prepared; return; }
            var claims = partition.Database.Verify<TenantPlacementPlanClaims>(request.SignedPlan,
                partition.Database.Limits.MaxBatchBytes);
            PlacementCapacityWitnessValidation.Require(claims, clock.GetUtcNow());
            var parts = claims.Plan.OriginalPartitions;
            var fresh = await collector.ReadAsync(principal, parts, work, trusted.ExpiresAt, current)
                .ConfigureAwait(false);
            PlacementPersistenceCohort.Require(claims, fresh, clock.GetUtcNow());
            result = partition.Database.CreateVerifiedPlacementIntentPersistence(trusted.CommandId,
                principal.Id, request, fresh, fresh[FirstCapacityIndex].CohortFingerprint);
            work.Check();
        }, token).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed);
    }
}
