namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementNativeTransferOperations
{
    internal static async Task<PartitionMovementGrainServiceReply> ExecuteAsync(PartitionMovementNativeReceiver owner,
        PartitionMovementGrainServiceRequest packet, CancellationToken cancellationToken)
    {
        var request = await owner.Admission.VerifyTransferDataAsync(packet.Payload, packet.Signature, cancellationToken).ConfigureAwait(false);
        return await PartitionMovementNativeLifetime.WithinExpiryAsync(owner, request.ExpiresAt, async token =>
        {
            var terminal = await PartitionMovementNativeReplies.ExecuteAsync(() => owner.Receiver.ExecuteTransferDataAsync(request, token)).ConfigureAwait(false);
            var reply = new PartitionMovementTransferDataReply(request.RequestId, request.Nonce,
                owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal);
            return PartitionMovementNativeReplies.Encode(owner, reply, static (mac, bytes) => mac.SignTransferData(bytes, reply: true));
        }, cancellationToken).ConfigureAwait(false);
    }
}
