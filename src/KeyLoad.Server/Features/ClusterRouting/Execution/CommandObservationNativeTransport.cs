using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Directs every follow-up to the exact pre-effect claim generation with no directory refresh.</summary>
internal sealed class CommandObservationNativeTransport(PartitionHost partition, IOptions<NodeOptions> options,
    ReplicaSiloDiscoveryState local, PartitionMovementGrainServiceClient movement) : INativeCommandObservationTransport
{
    public Task<GrainOperationReply> ExecuteAsync(string opaqueToken, CancellationToken token)
        => SendAsync(CommandObservationNativeAction.Execute, opaqueToken, token);

    public Task<GrainOperationReply> ReadAsync(string opaqueToken, CancellationToken token)
        => SendAsync(CommandObservationNativeAction.Read, opaqueToken, token);

    public async Task AbortAsync(string opaqueToken, CancellationToken token)
    {
        var reply = await SendAsync(CommandObservationNativeAction.Abort, opaqueToken, token).ConfigureAwait(false);
        if (reply.Error is not null || !reply.Payload.IsEmpty || reply.SafeDetail is not null || reply.WorkProof is not null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, CommandObservationNativeProtocol.Unavailable);
        }
    }

    private async Task<GrainOperationReply> SendAsync(CommandObservationNativeAction action,
        string opaqueToken, CancellationToken token)
    {
        var handle = NativeCommandObservationHandleCodec.Verify(partition.Database, opaqueToken);
        var current = local.Read();
        var configured = PhysicalOwnerConfiguredTuples.Local(options.Value, partition.Configuration);
        if (!current.TransportReady || handle.Runtime is not { TransportReady: true }
            || !PhysicalOwnerEntryValidation.SameOwner(handle.PhysicalOwner, configured.Owner)
            || handle.Runtime.ClusterId != current.ClusterId || handle.Runtime.Incarnation != current.Incarnation
            || !configured.Owner.VoterIds.Contains(handle.Runtime.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, CommandObservationNativeProtocol.Unavailable);
        }
        var address = SiloAddress.FromParsableString(handle.Runtime.SiloAddress);
        var request = new CommandObservationNativeRequest(CommandObservationNativeProtocol.Version,
            action, opaqueToken, current, handle.Runtime, Guid.NewGuid(), string.Empty);
        CommandObservationNativeMac.RequireSize(request, partition);
        using var mac = CommandObservationNativeMac.Create(options.Value, partition);
        request = request with
        {
            Signature = CommandObservationNativeMac.Sign(mac, request)
        };
        var reply = await movement.ExecuteOriginalCommandObservationPacketAsync(address, request, token).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, CommandObservationNativeProtocol.Invalid);
        CommandObservationNativeMac.RequireSize(reply, partition);
        if (reply.Nonce != request.Nonce || reply.RequestSignature != request.Signature
            || reply.Result is null || !CommandObservationNativeMac.Verify(mac, reply))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, CommandObservationNativeProtocol.Invalid);
        }
        return reply.Result;
    }
}
