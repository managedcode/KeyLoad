using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementNativeIssuanceOperations
{
    internal static async Task<PartitionMovementGrainServiceReply> ExecuteAsync(PartitionMovementNativeReceiver owner,
        PartitionMovementGrainServiceRequest packet, CancellationToken cancellationToken)
    {
        if (packet.Query)
        {
            var requested = await owner.Admission.VerifyReceiverIssueQueryAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
            return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, requested.QueryExpiresAt, async token =>
            {
                var work = new ReadExecutionBudget(owner.Limits, owner.Clock, token);
                var witness = await owner.Receiver.ReadReceiverIssuanceProofAsync(requested, work, token).ConfigureAwait(false);
                return PartitionMovementNativeReplies.Original(owner, witness.OriginalReplyBytes, witness.OriginalReplySignature);
            }, cancellationToken).ConfigureAwait(false);
        }
        var request = await owner.Admission.VerifyReceiverIssueAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
        return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, request.OriginalEnvelope.ExpiresAt, async token =>
        {
            var work = new ReadExecutionBudget(owner.Limits, owner.Clock, token);
            var terminal = await PartitionMovementNativeReplies.ExecuteAsync(() => owner.Receiver.IssueReceiverAsync(request,
                packet.Payload, packet.Signature, work, token)).ConfigureAwait(false);
            var reply = new PartitionMovementTransportReply(request.OriginalPhaseCommandId, request.IssuanceNonce,
                owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
                PartitionMoveOriginalDispatchIdentity.Digest(request.OriginalPhaseCommandId, request.OriginalEnvelope));
            return PartitionMovementNativeReplies.Encode(owner, reply, static (mac, bytes) => mac.SignReceiverIssue(bytes, source: false));
        }, cancellationToken).ConfigureAwait(false);
    }
}
