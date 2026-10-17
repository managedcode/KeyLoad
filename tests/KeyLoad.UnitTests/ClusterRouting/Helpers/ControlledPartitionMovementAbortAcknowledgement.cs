using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Acknowledges only the original actually returned receiver abort journal at its control owner.</summary>
internal static class ControlledPartitionMovementAbortAcknowledgement
{
    private const int Version = 1;
    private const int ControlOrdinal = 0;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult aborting,
        PartitionMovePhaseResult originalResult, PartitionMoveCleanupRole role, int batchOrdinal,
        string originalCallerAddress, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        var stage = originalResult.Stage;
        var grantId = ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, batchOrdinal);
        var control = aborting.Control
            ?? throw new InvalidOperationException("The actual original Aborting control is absent.");
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(grantId, originalResult.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            aborting.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            ControlOrdinal, originalExpiry, Guid.NewGuid(), body);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(ControlledPartitionMovementAbortPhaseIds.Acknowledgement(stage, role, batchOrdinal), envelope,
                null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, ControlOrdinal), cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var grant = outcome.Get<PartitionMovePhaseResult>().Grant
            ?? throw new InvalidOperationException("The actual original abort settlement is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(grantId);
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(originalResult.Journal))).IsTrue();
    }
}
