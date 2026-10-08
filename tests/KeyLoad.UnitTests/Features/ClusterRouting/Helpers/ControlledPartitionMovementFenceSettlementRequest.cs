using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Builds original local-control requests using only the actual returned native source fence receipt.</summary>
internal static class ControlledPartitionMovementFenceSettlementRequest
{
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    internal static readonly Guid AcknowledgeId = Guid.Parse("fe2e8cb6-cf4a-4319-bcc5-7ae8be0eb4e9");
    internal static readonly Guid AcceptFenceId = Guid.Parse("3ff1c9e0-f0ac-40a7-a44e-9c45e91d804c");

    internal static PartitionMovementTransportRequest Acknowledge(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualFence, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
        => Create(AcknowledgeId, PartitionMovePeerStage.ControlAcknowledge, actualPrepared, corpus,
            originalCallerAddress, originalExpiry, NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
                ControlledPartitionMovementFenceRequest.GrantId, actualFence.Journal)));

    internal static PartitionMovementTransportRequest Accept(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualFence, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException("The actual Prepared control is required.");
        var fence = actualFence.Fence
            ?? throw new InvalidOperationException("The actual logged source fence is required.");
        return Create(AcceptFenceId, PartitionMovePeerStage.ControlAcceptFence, actualPrepared, corpus,
            originalCallerAddress, originalExpiry, NativeSerialization.Serialize(new PartitionMoveFenceAcceptBody(
                PhysicalShardCatalogFixture.RootPrincipalId, control, fence,
                ControlledPartitionMovementFenceRequest.GrantId)));
    }

    private static PartitionMovementTransportRequest Create(Guid commandId, PartitionMovePeerStage stage,
        PartitionMovePhaseResult actualPrepared, ControlledPartitionMovementLoopbackCorpus corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry, byte[] body)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException("The actual Prepared control is required.");
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            actualPrepared.Journal.ControlIntentDigest, stage, InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        return new(commandId, envelope, null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }
}
