using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementRuntime
{
    internal async Task<PartitionMovementDispatchResult> DispatchProtectedDocumentAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        ReadExecutionBudget originalWork, CancellationToken cancellationToken)
    {
        PartitionMovementDispatchResult? result = null;
        await RunAsync(async token =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var scope = originalWork.EnterStageCancellation(token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var sender = client ?? throw Errors.Fail(ErrorCode.OwnershipLost,
                        PartitionMovementProtocol.Unavailable);
                    result = await sender.DispatchProtectedDocumentAsync(phaseCommandId, original,
                        authorization, originalWork, token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, cancellationToken).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }
}
