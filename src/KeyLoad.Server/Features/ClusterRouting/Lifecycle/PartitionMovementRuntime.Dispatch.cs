using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementRuntime
{
    public async Task<PartitionMovementDispatchResult> DispatchAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken)
    {
        PartitionMovementDispatchResult? result = null;
        await RunAsync(async token =>
        {
            var sender = client ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
            result = await sender.DispatchAsync(phaseCommandId, original, authorization, action, handleId,
                ordinal, pinnedVoter, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }
}
