using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Logs the actual source receipt at A before independently verifying Fenced control publication.</summary>
internal static class ControlledPartitionMovementFenceSettlementFlow
{
    private const long AcknowledgeReplicaIndex = 14;
    private const long AcceptedFenceReplicaIndex = 15;
    private const long SourceFenceReplicaIndex = 13;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult prepared,
        PartitionMovePhaseResult authorization, PartitionMovePhaseResult fence, string originalCallerAddress,
        DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        var ackRequest = ControlledPartitionMovementFenceSettlementRequest.Acknowledge(prepared, fence,
            corpus, originalCallerAddress, originalExpiry);
        var acknowledgedOutcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, ackRequest, cancellationToken);
        await Assert.That(acknowledgedOutcome.Error).IsNull();
        var grant = authorization.Grant ?? throw new InvalidOperationException("The actual original A grant is absent.");
        var expectedAck = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlAcknowledge, new(ControlledPartitionMovementFenceSettlementRequest.AcknowledgeId,
                corpus.Control.Owner, AcknowledgeReplicaIndex, prepared.Journal.ControlIntentDigest),
            null, null, null, null, grant with { Settlement = fence.Journal });
        await Assert.That(JsonDefaults.Serialize(acknowledgedOutcome.Get<PartitionMovePhaseResult>())
            .SequenceEqual(JsonDefaults.Serialize(expectedAck))).IsTrue();
        var acceptedOutcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
            admission, ControlledPartitionMovementFenceSettlementRequest.Accept(prepared, fence, corpus,
                originalCallerAddress, originalExpiry), cancellationToken);
        await Assert.That(acceptedOutcome.Error).IsNull();
        var control = prepared.Control ?? throw new InvalidOperationException("The actual Prepared control is absent.");
        var expectedAccepted = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlAcceptFence, new(ControlledPartitionMovementFenceSettlementRequest.AcceptFenceId,
                corpus.Control.Owner, AcceptedFenceReplicaIndex, prepared.Journal.ControlIntentDigest),
            control with { Phase = PartitionMovePhase.Fenced, SourceCut = SourceFenceReplicaIndex },
            fence.Fence, null, null);
        var accepted = acceptedOutcome.Get<PartitionMovePhaseResult>();
        await Assert.That(JsonDefaults.Serialize(accepted).SequenceEqual(JsonDefaults.Serialize(expectedAccepted))).IsTrue();
        return accepted;
    }
}
