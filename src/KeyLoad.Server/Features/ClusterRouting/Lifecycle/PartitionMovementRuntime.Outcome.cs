using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementRuntime
{
    public async Task<PartitionMovementOutcomeWitness> QueryOutcomeAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization, string originalVoter,
        DateTimeOffset requestExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        PartitionMovementOutcomeWitness? result = null;
        await RunAsync(async token =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var stage = work.EnterStageCancellation(token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var sender = client ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable);
                    result = await sender.QueryOutcomeAsync(phaseCommandId, original, authorization,
                        originalVoter, requestExpiry, work, token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, cancellationToken).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }
}
