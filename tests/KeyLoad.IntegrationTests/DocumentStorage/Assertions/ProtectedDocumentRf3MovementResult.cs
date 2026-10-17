using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DocumentStorage.ProtectedDocumentRf3MovementProtocol;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class ProtectedDocumentRf3MovementResult
{
    internal static async Task RequireAsync(ProtectedDocumentRf3MovementState movement,
        AtomicPartitionPlacementResolution original, CommitReceipt seed, NodeStatus beforeSource, NodeStatus beforeTarget)
    {
        var actual = movement.Receipt;
        var token = actual.InstalledReceipt ?? throw new InvalidOperationException(P.MissingReceipt);
        await Assert.That(original.PhysicalShardId).IsEqualTo(movement.Source.PhysicalShardId);
        await Assert.That(original.Incarnation).IsEqualTo(movement.Source.Incarnation);
        await Assert.That(seed.Token.Incarnation).IsEqualTo(movement.Source.Incarnation);
        await Assert.That(seed.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await Assert.That(seed.Token.OwnershipEpoch).IsEqualTo(original.PlacementEpoch);
        await Assert.That(seed.Token.Position).IsGreaterThan(P.ZeroPosition);
        await Assert.That(seed.Token.Position).IsLessThanOrEqualTo(beforeSource.Applied);
        await Assert.That(beforeSource.Incarnation).IsEqualTo(movement.Source.Incarnation);
        await Assert.That(beforeTarget.Incarnation).IsEqualTo(movement.Target.Incarnation);
        await Assert.That(actual.SourceCut).IsGreaterThanOrEqualTo(beforeSource.Applied);
        await Assert.That(actual.SourceCut).IsGreaterThanOrEqualTo(seed.Token.Position);
        await Assert.That(actual.SourceCut).IsGreaterThan(P.ZeroPosition);
        await Assert.That(token.Position).IsGreaterThan(beforeTarget.Applied);
        await Assert.That(token.Incarnation).IsEqualTo(movement.Target.Incarnation);
        await Assert.That(token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await Assert.That(token.OwnershipEpoch).IsEqualTo(movement.Target.PlacementEpoch);
        await Assert.That(movement.TargetCut.Incarnation).IsEqualTo(movement.Target.Incarnation);
        await Assert.That(movement.TargetCut.Applied).IsGreaterThanOrEqualTo(token.Position);
        var expectedPlacement = new AtomicPartitionPlacementV1(P.Version, original.Partition,
            movement.Target.PhysicalShardId, checked(original.DirectoryRevision + P.NextRevision), movement.Target.Incarnation,
            movement.Target.VoterIds, checked(Math.Max(original.PlacementEpoch, movement.Target.PlacementEpoch) + P.NextRevision));
        await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(movement.Request.MoveId, original.Partition,
            PartitionMovePhase.Retired, movement.Source, movement.Target, actual.SourceCut, token, expectedPlacement), actual);
    }

    internal static async Task PlacementAsync(KeyLoadClient source,
        ProtectedDocumentRf3MovementState movement, AtomicPartitionPlacementResolution original, CancellationToken token)
    {
        var expected = movement.Receipt.PublishedPlacement ?? throw new InvalidOperationException(P.MissingReceipt);
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(P.Version, original.Partition,
            movement.Target.PhysicalShardId, movement.Target.Incarnation, movement.Target.VoterIds,
            expected.PlacementEpoch, checked(original.DirectoryRevision + P.NextRevision), expected.Revision, false),
            await McpCallerAssertions.SdkSuccessAsync(await source.ReadAtomicPartitionPlacementAsync(new(P.Version, original.Partition), token)));
    }
}
