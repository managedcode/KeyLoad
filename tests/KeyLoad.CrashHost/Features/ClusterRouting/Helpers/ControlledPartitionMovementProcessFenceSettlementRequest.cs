using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Builds original local-control requests using only the actual returned native source fence receipt.</summary>
internal static class ControlledPartitionMovementProcessFenceSettlementRequest
{
    private const string AcknowledgeIdText = "fe2e8cb6-cf4a-4319-bcc5-7ae8be0eb4e9";
    private const string AcceptFenceIdText = "3ff1c9e0-f0ac-40a7-a44e-9c45e91d804c";
    private const string PreparedControlRequired = "The actual Prepared control is required.";
    private const string SourceFenceRequired = "The actual logged source fence is required.";
    private const string PrincipalId = "root";
    private const int Version = 1;
    private const int InitialOrdinal = 0;
    internal static readonly Guid AcknowledgeId = Guid.Parse(AcknowledgeIdText);
    internal static readonly Guid AcceptFenceId = Guid.Parse(AcceptFenceIdText);

    internal static PartitionMovementTransportRequest Acknowledge(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualFence, ControlledPartitionMovementProcessOwners corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
        => Create(AcknowledgeId, PartitionMovePeerStage.ControlAcknowledge, actualPrepared, corpus,
            originalCallerAddress, originalExpiry, NativeSerialization.Serialize(new PartitionMoveAcknowledgeBody(
                ControlledPartitionMovementProcessFenceRequest.GrantId, actualFence.Journal)));

    internal static PartitionMovementTransportRequest Accept(PartitionMovePhaseResult actualPrepared,
        PartitionMovePhaseResult actualFence, ControlledPartitionMovementProcessOwners corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException(PreparedControlRequired);
        var fence = actualFence.Fence
            ?? throw new InvalidOperationException(SourceFenceRequired);
        return Create(AcceptFenceId, PartitionMovePeerStage.ControlAcceptFence, actualPrepared, corpus,
            originalCallerAddress, originalExpiry, NativeSerialization.Serialize(new PartitionMoveFenceAcceptBody(
                PrincipalId, control, fence,
                ControlledPartitionMovementProcessFenceRequest.GrantId)));
    }

    private static PartitionMovementTransportRequest Create(Guid commandId, PartitionMovePeerStage stage,
        PartitionMovePhaseResult actualPrepared, ControlledPartitionMovementProcessOwners corpus,
        string originalCallerAddress, DateTimeOffset originalExpiry, byte[] body)
    {
        var control = actualPrepared.Control
            ?? throw new InvalidOperationException(PreparedControlRequired);
        var envelope = new PartitionMovePeerEnvelope(Version, control.MoveId, control.Partition,
            corpus.Control.Owner, control.SourcePlacement, corpus.Destination.Owner,
            actualPrepared.Journal.ControlIntentDigest, stage, InitialOrdinal, originalExpiry, Guid.NewGuid(), body);
        return new(commandId, envelope, null, corpus.Control.Owner.VoterIds.First(), originalCallerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }
}
