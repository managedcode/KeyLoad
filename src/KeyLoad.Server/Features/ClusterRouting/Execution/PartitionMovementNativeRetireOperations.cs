using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementNativeRetireOperations
{
    internal static async Task<PartitionMovementGrainServiceReply> ExecuteAsync(PartitionMovementNativeReceiver owner,
        PartitionMovementGrainServiceRequest packet, CancellationToken cancellationToken)
    {
        if (packet.Query)
        {
            var requested = await owner.Admission.VerifyRetireCancellationQueryAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
            return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, requested.QueryExpiresAt, async token =>
            {
                var work = new ReadExecutionBudget(owner.Limits, owner.Clock, token);
                var reply = await owner.Receiver.ReadRetireCancellationAsync(requested, packet.Payload, packet.Signature, work, token).ConfigureAwait(false);
                return Encode(owner, reply);
            }, cancellationToken).ConfigureAwait(false);
        }
        var request = await owner.Admission.VerifyRetireCancellationAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
        return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, request.CancellationExpiresAt, async token =>
        {
            var work = new ReadExecutionBudget(owner.Limits, owner.Clock, token);
            var terminal = await PartitionMovementNativeReplies.ExecuteAsync(() => owner.Receiver.CancelExpiredRetireAsync(request,
                packet.Payload, packet.Signature, work, token)).ConfigureAwait(false);
            var reply = new PartitionMovementTransportReply(request.CancellationCommandId, request.CancellationNonce,
                owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
                PartitionMoveOriginalDispatchIdentity.Digest(request.OriginalPhaseCommandId, request.OriginalEnvelope));
            return Encode(owner, reply);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static PartitionMovementGrainServiceReply Encode(PartitionMovementNativeReceiver owner, PartitionMovementTransportReply reply)
    {
        if (NativeSerialization.Measure(reply) > owner.Limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var bytes = NativeSerialization.Serialize(reply);
        return PartitionMovementNativeReplies.Original(owner, bytes, owner.Receiver.SignRetireCancellationReply(bytes));
    }
}
