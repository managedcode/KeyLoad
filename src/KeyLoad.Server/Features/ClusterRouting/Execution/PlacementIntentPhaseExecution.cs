using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PlacementIntentPhaseExecution(OrleansNode node, PartitionHost partition, TimeProvider clock)
{
    private const int CompleteOwnerCohort = 6;
    internal async Task<PlacementIntentV1> ExecuteAsync(string principalId, PlacementIntentPhaseRequestV1 phase,
        DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken token)
    {
        var current = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        var local = node.Discovery?.Read();
        if (current is null || current.ActiveSilos != CompleteOwnerCohort || current.ActiveFingerprint != phase.Claim.CohortFingerprint
            || local is null || !local.TransportReady || local.VoterId != phase.Claim.VoterId
            || local.SiloAddress != phase.Claim.SiloAddress || phase.Claim.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort);
        }
        var commandId = Guid.NewGuid();
        var operation = partition.Database.CreateVerifiedPlacementIntentPhase(commandId, principalId, phase, work);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreatePlacementIntentPhase(requestId, operation, originalExpiry);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view, principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId, commandId, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await node.ExecuteAsync(requestId, signed, command: true, token).ConfigureAwait(false);
            }, failures)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (reply is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed);
        }
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? TenantPlacementPolicyProtocol.ScopeMismatch);
        }
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PlacementIntentV1
            ?? throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed);
    }
}
