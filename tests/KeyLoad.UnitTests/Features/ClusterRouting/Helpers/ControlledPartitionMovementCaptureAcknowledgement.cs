using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Acknowledges only an original Capture receipt returned after actual source image/page retirement.</summary>
internal static class ControlledPartitionMovementCaptureAcknowledgement
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    private static readonly Guid CommandId = Guid.Parse("bb84e3ca-7faa-4287-a0c5-7e611dbe5894");

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        ControlledPartitionMovementLoopbackCorpus corpus, PartitionMovePhaseResult acceptedFence,
        PartitionMovePhaseResult originalSettlement, string callerAddress, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
    {
        if (originalSettlement.Stage != PartitionMovePeerStage.Capture
            || originalSettlement.Journal.CommandId != ControlledPartitionMovementCaptureRequest.CaptureCommandId)
        { throw new InvalidOperationException("The original actual Capture settlement is required."); }
        var control = acceptedFence.Control
            ?? throw new InvalidOperationException("The actual Fenced control is required.");
        var body = NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
            ControlledPartitionMovementCaptureRequest.GrantId, originalSettlement.Journal));
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            acceptedFence.Journal.ControlIntentDigest, PartitionMovePeerStage.ControlAcknowledge,
            InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
                PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal), cancellationToken);
        await Assert.That(result.Error).IsNull();
        var actual = result.Get<PartitionMovePhaseResult>();
        var grant = actual.Grant ?? throw new InvalidOperationException("The actual settled A grant is absent.");
        await Assert.That(grant.GrantId).IsEqualTo(ControlledPartitionMovementCaptureRequest.GrantId);
        await Assert.That(JsonDefaults.Serialize(grant.Settlement)
            .SequenceEqual(JsonDefaults.Serialize(originalSettlement.Journal))).IsTrue();
        return actual;
    }
}
