using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PlacementCapacityCollector(OrleansNode node, PartitionHost partition,
    PartitionMovementPeerAdmission movement, TimeProvider clock)
{
    private const int FirstVoterIndex = 0;
    private const int CompleteCohort = 6;

    internal async Task<ImmutableArray<OwnerCapacitySnapshotV1>> ReadAsync(PrincipalRecord principal,
        ImmutableArray<PartitionRef> parts, ReadExecutionBudget work, DateTimeOffset originalExpiry, CancellationToken token)
    {
        var membership = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        if (membership is null || membership.ActiveSilos != CompleteCohort)
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
        var owners = new[]
        {
            PhysicalOwnerConfiguredTuples.Control(movement.Options, movement.Configuration),
            PhysicalOwnerConfiguredTuples.Destination(movement.Options, movement.Configuration)
        };
        var result = ImmutableArray.CreateBuilder<OwnerCapacitySnapshotV1>(CompleteCohort);
        foreach (var owner in owners)
        {
            for (var index = FirstVoterIndex; index < owner.Owner.VoterIds.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
                OwnerCapacitySnapshotV1? value = null;
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    value = await node.BorrowPartitionMovementTransport().ReadCapacityAsync(owner, index,
                        principal, membership.ActiveFingerprint, parts, originalExpiry, grant.RemainingBytes, grant.RemainingRecords,
                        partition.Database.Limits.MaxBatchBytes, token).ConfigureAwait(false);
                    work.ImportSettledReadGrant(grant, value.NativeReadBytes, value.NativeExaminedRecords);
                }, failures).ConfigureAwait(false);
                ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
                ServerFailureObserver.ThrowIfAny(failures);
                result.Add(value ?? throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed));
            }
        }
        var after = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        if (after is null || after.ActiveSilos != CompleteCohort
            || after.ActiveFingerprint != membership.ActiveFingerprint || originalExpiry <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
        return PlacementCapacityCohort.Require(result.MoveToImmutable(), after.ActiveFingerprint, clock.GetUtcNow());
    }
}
