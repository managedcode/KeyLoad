using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Independent complete literal prepared-control and original outcome oracles.</summary>
internal static class ControlledPartitionMovementPrepareAssertions
{
    private const int Version = 1;
    private const long InitialEpoch = 1;
    private const long EmptyRevision = 0;
    private const long EmptyCut = 0;
    internal const long PreparedReplicaIndex = 11;
    private const string ChangedContent = "The command ID was already used with different content.";

    internal static AtomicPartitionPlacementResolution Placement(ControlledPartitionMovementLoopbackCorpus corpus)
        => new(Version, ControlledPartitionMovementCorpus.Partition, corpus.Control.Owner.PhysicalShardId,
            corpus.Control.Owner.Incarnation, corpus.Control.Owner.VoterIds, InitialEpoch, EmptyRevision,
            EmptyRevision, true);

    internal static async Task<PartitionMovePhaseResult> PreparedAsync(OperationResult result,
        ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var expectedControl = new PartitionMoveControlRecord(Version, ControlledPartitionMovementPrepareRequest.MoveId,
            ControlledPartitionMovementCorpus.Partition, PhysicalShardCatalogFixture.RootPrincipalId, InitialEpoch,
            Placement(corpus), corpus.Destination.Owner, PartitionMovePhase.Prepared, EmptyCut,
            PreparedReplicaIndex, null, null, null);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(new PartitionMoveIntent(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, ControlledPartitionMovementCorpus.Partition,
            PhysicalShardCatalogFixture.RootPrincipalId, InitialEpoch, Placement(corpus),
            corpus.Destination.Owner, PreparedReplicaIndex))));
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.ControlPrepare, new(ControlledPartitionMovementPrepareRequest.CommandId,
                corpus.Control.Owner, PreparedReplicaIndex, digest), expectedControl, null, null, null);
        await Assert.That(result.Error).IsNull();
        var actual = result.Get<PartitionMovePhaseResult>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return actual;
    }

    internal static async Task ConflictAsync(OperationResult conflict)
    {
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(conflict.SafeDetail).IsEqualTo(ChangedContent);
        await Assert.That(conflict.Json).IsNull();
        await Assert.That(conflict.NativeValue).IsNull();
    }
}
