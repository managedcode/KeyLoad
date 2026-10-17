using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Logs the real source capture-closed marker only after the target terminal journal was acknowledged at A.</summary>
internal static class ControlledPartitionMovementSourceAbortMarkerFlow
{
    private const int Version = 1;
    private const int InitialBatch = 0;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult aborting,
        Guid actualSettledTargetGrantId, string originalCallerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        var terminalOrdinal = PartitionMoveCleanupFamilies.All.Length;
        var stage = PartitionMovePeerStage.SourceBeginAbort;
        var role = PartitionMoveCleanupRole.Source;
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            ControlledPartitionMovementAbortRequest.Authorize(aborting, stage, role, terminalOrdinal,
                InitialBatch, actualSettledTargetGrantId, corpus, originalCallerAddress, originalExpiry),
            cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementAbortRequest.Apply(aborting, stage, role, terminalOrdinal,
            InitialBatch, actualSettledTargetGrantId, authorization.Get<PartitionMovePhaseResult>(), corpus,
            originalCallerAddress, originalExpiry);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            request, cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var expected = new PartitionMoveCleanupState(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, ControlledPartitionMovementCorpus.Partition,
            aborting.Journal.ControlIntentDigest, PartitionMovePeerStage.Abort, role,
            terminalOrdinal, null, InitialBatch);
        await Assert.That(JsonDefaults.Serialize(actual.Cleanup)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(actual.Journal.CommandId).IsEqualTo(request.CommandId);
        await ControlledPartitionMovementAbortAcknowledgement.ExecuteAsync(source, runtime, admission,
            corpus, aborting, actual, role, InitialBatch, originalCallerAddress, originalExpiry,
            cancellationToken);
        return actual;
    }
}
