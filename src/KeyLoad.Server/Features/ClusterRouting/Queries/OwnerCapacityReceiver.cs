using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class OwnerCapacityReceiver(OrleansNode node, PartitionHost partition,
    OwnerCapacityPeerAdmission admission, OwnerCapacitySignedReads reads,
    OwnerCapacityResourceSampler sampler, PartitionMovementPeerAdmission movement,
    IOptions<TenantPlacementExecutionOptions> options)
{
    private const int CompleteOwnerSilos = 6;

    internal async Task<OwnerCapacityNativeReply> ReadAsync(OwnerCapacityNativeRequest request, CancellationToken token)
    {
        var authenticated = false;
        try
        {
            var local = node.Discovery?.Read()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.Unavailable);
            await admission.VerifyAsync(request, local, (voter, address, caller) =>
                node.BorrowPartitionMovementTransport().RequireCallerAsync(voter, address, null, caller),
                token).ConfigureAwait(false);
            authenticated = true;
            admission.RequireExpiryReplay(request, token);
            await RequireCohortAsync(request, token).ConfigureAwait(false);
            await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
            await sampler.AwaitOriginalObservationAsync(token).ConfigureAwait(false);
            var resource = sampler.Read(local.SiloAddress);
            var read = new OwnerCapacityReadRequestV1(TenantPlacementPolicyProtocol.Version,
                request.RequestId, request.Nonce, request.Destination.Owner, local.VoterId, local.SiloAddress,
                request.CohortFingerprint, request.ExpiresAt, request.Partitions, resource, request.PolicyEpoch,
                Math.Min(request.MaximumReadBytes, Math.Min(options.Value.MaximumAccountingBytes, partition.Database.Limits.MaxQueryReadBytes)),
                Math.Min(request.MaximumExaminedRecords, Math.Min(options.Value.MaximumAccountingRecords, partition.Database.Limits.MaxScanRecords)),
                Math.Min(request.MaximumResultBytes, partition.Database.Limits.MaxBatchBytes));
            var value = await reads.ReadAsync(read, request.PrincipalId, token).ConfigureAwait(false);
            await RequireCohortAsync(request, token).ConfigureAwait(false);
            if (request.ExpiresAt <= movement.Clock.GetUtcNow())
            { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementCapacityProtocol.InvalidProof); }
            return Sign(new(TenantPlacementPolicyProtocol.Version, request.RequestId, request.Nonce,
                value, null, null, string.Empty));
        }
        catch (KeyLoadException error)
        { return Failure(request, authenticated, error.Code); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { throw; }
    }

    private async Task RequireCohortAsync(OwnerCapacityNativeRequest request, CancellationToken token)
    {
        var actual = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        var local = node.Discovery?.Read();
        if (actual is null || actual.ActiveSilos != CompleteOwnerSilos
            || actual.ActiveFingerprint != request.CohortFingerprint || local is null
            || !local.TransportReady || local.VoterId != request.DestinationVoter
            || local.SiloAddress != request.DestinationSiloAddress
            || local.Incarnation != request.Destination.Owner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
    }

    private OwnerCapacityNativeReply Failure(OwnerCapacityNativeRequest? request, bool authenticated, ErrorCode code)
    {
        var reply = new OwnerCapacityNativeReply(TenantPlacementPolicyProtocol.Version,
            request?.RequestId ?? Guid.Empty, request?.Nonce ?? Guid.Empty, null, code,
            TenantPlacementCapacityProtocol.Unavailable, string.Empty);
        return authenticated ? Sign(reply) : reply;
    }

    private OwnerCapacityNativeReply Sign(OwnerCapacityNativeReply reply)
    {
        using var mac = movement.CreateControlMac();
        return reply with { Signature = OwnerCapacityMac.SignCapacityReply(mac, reply) };
    }
}
