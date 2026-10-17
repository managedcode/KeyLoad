using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Settles every real target cleanup family and retains its exact original terminal body and journal.</summary>
internal static class ControlledPartitionMovementTargetAbortFlow
{
    private const int FirstFamily = 0;
    private const int FamilyStep = 1;

    internal static async Task<(PartitionMovePhaseResult Terminal, Guid GrantId,
        ReadOnlyMemory<byte> OriginalBody)> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions sourceRuntime,
        ServerRuntimeOptions targetRuntime, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult aborting, string callerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        var terminalOrdinal = PartitionMoveCleanupFamilies.All.Length;
        for (var family = FirstFamily; family <= terminalOrdinal; family += FamilyStep)
        {
            var sourceIndex = source.Journal.Log.State.LastIndex;
            var targetIndex = target.Journal.Log.State.LastIndex;
            var stage = PartitionMovePeerStage.Abort;
            var role = PartitionMoveCleanupRole.Target;
            var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, sourceRuntime,
                sourceAdmission, ControlledPartitionMovementAbortRequest.Authorize(aborting, stage, role,
                    family, family, Guid.Empty, corpus, callerAddress, originalExpiry), cancellationToken);
            await Assert.That(authorization.Error).IsNull();
            var request = ControlledPartitionMovementAbortRequest.Apply(aborting, stage, role,
                family, family, Guid.Empty, authorization.Get<PartitionMovePhaseResult>(), corpus,
                callerAddress, originalExpiry);
            var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(target, targetRuntime,
                targetAdmission, request, cancellationToken);
            await Assert.That(outcome.Error).IsNull();
            var actual = outcome.Get<PartitionMovePhaseResult>();
            await ControlledPartitionMovementAbortStateAssertions.AssertAsync(actual, role,
                family, terminalOrdinal, aborting.Journal.ControlIntentDigest);
            await ControlledPartitionMovementAbortAcknowledgement.ExecuteAsync(source, sourceRuntime,
                sourceAdmission, corpus, aborting, actual, role, family, callerAddress,
                originalExpiry, cancellationToken);
            await PartitionMovementCleanupCountAssertions.AbortAsync(source, target, sourceIndex, targetIndex);
            if (family == terminalOrdinal)
            {
                return (actual, ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, family),
                    request.Envelope.Body);
            }
        }
        throw new InvalidOperationException("The actual terminal target abort journal is absent.");
    }
}
