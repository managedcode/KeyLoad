using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Preserves complete literal phase oracles separately from native wire failure shapes.</summary>
internal static class ControlledPartitionMovementNativeReplyAssertions
{
    private const int Version = 1;
    private const long PolicyEpoch = 1;
    private const long EmptyCut = 0;

    internal static async Task<PartitionMovePhaseResult> PreparedAsync(
        PartitionMovementAuthenticatedReply original, AtomicPartitionPlacementResolution placement,
        PhysicalShardRecord source, PhysicalShardRecord destination, long independentlyCommittedIndex)
    {
        await Assert.That(independentlyCommittedIndex).IsGreaterThan(EmptyCut);
        await Assert.That(original.Value.CommandId).IsEqualTo(ControlledPartitionMovementPrepareRequest.CommandId);
        await Assert.That(original.Value.Reply.Error).IsNull();
        await Assert.That(original.Value.Reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(original.Value.Reply.Payload).Value
            as PartitionMovePhaseResult;
        await Assert.That(actual).IsNotNull();
        var intent = new PartitionMoveIntent(Version, ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, PhysicalShardCatalogFixture.RootPrincipalId,
            PolicyEpoch, placement, destination, independentlyCommittedIndex);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(intent)));
        var control = new PartitionMoveControlRecord(Version, ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, PhysicalShardCatalogFixture.RootPrincipalId,
            PolicyEpoch, placement, destination, PartitionMovePhase.Prepared, EmptyCut,
            independentlyCommittedIndex, null, null, null);
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlPrepare, new(ControlledPartitionMovementPrepareRequest.CommandId,
                source, independentlyCommittedIndex, digest), control, null, null, null);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return expected;
    }

    internal static async Task RefusedAsync(PartitionMovementAuthenticatedReply original, ErrorCode expected)
    {
        await Assert.That(original.Value.Reply.Error).IsEqualTo(expected);
        await Assert.That(original.Value.Reply.SafeDetail).IsEqualTo(PartitionMovementProtocol.Unavailable);
        await Assert.That(original.Value.Reply.Payload.IsEmpty).IsTrue();
    }
}
