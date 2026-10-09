using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual fixed-control public parent moves linked native models A to B, then B to A after true cold replay.</summary>
internal static class PartitionMovementPublicParentRf3Scenario
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var first = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.FirstRequest, cancellationToken).ConfigureAwait(false));
        await RequireTerminalAsync(seed, seed.FirstRequest, seed.OriginalPlacement,
            seed.Directory.ControlOwner, first, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        await ColdReplayAsync(wave, seed, seed.FirstRequest, first, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3OmissionTrial.ExecuteAsync(wave, seed, seed.FirstRequest, first,
            cancellationToken).ConfigureAwait(false);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(1, seed.Partition), cancellationToken).ConfigureAwait(false));
        var secondRequest = new PartitionMoveRequest(Guid.NewGuid(), seed.Partition,
            seed.Directory.ControlOwner.PhysicalShardId, placement.Revision, PartitionMoveMode.Transfer);
        var second = (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(await seed.Official.CallAsync(
            PartitionMovePublicProtocol.ToolName, secondRequest, cancellationToken).ConfigureAwait(false))).Value;
        var originalSource = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == placement.PhysicalShardId).Owner;
        await Assert.That(originalSource.PhysicalShardId).IsNotEqualTo(seed.Directory.ControlOwner.PhysicalShardId);
        await RequireTerminalAsync(seed, secondRequest, placement, originalSource, second,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        await ColdReplayAsync(wave, seed, secondRequest, second, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ColdReplayAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request, PartitionMoveResult actualTerminal,
        CancellationToken cancellationToken)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, request, actualTerminal,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request,
            cancellationToken).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task RequireTerminalAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveRequest request, AtomicPartitionPlacementResolution original, PhysicalShardRecord source,
        PartitionMoveResult actual, CancellationToken cancellationToken)
    {
        var target = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
            == request.DestinationPhysicalShardId).Owner;
        var token = actual.InstalledReceipt ?? throw new InvalidOperationException("Actual installed native receipt is absent.");
        await Assert.That(token.Position).IsGreaterThan(0L);
        await Assert.That(token.Incarnation).IsEqualTo(target.Incarnation);
        await Assert.That(token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(token.OwnershipEpoch).IsEqualTo(checked(original.PlacementEpoch + 1));
        await Assert.That(actual.SourceCut).IsGreaterThan(0L);
        var expectedPlacement = new AtomicPartitionPlacementV1(1, seed.Partition, target.PhysicalShardId,
            checked(original.Revision + 1), target.Incarnation, target.VoterIds, checked(original.PlacementEpoch + 1));
        await SqlRf3Protocol.EqualAsync(new PartitionMoveResult(request.MoveId, seed.Partition,
            PartitionMovePhase.Retired, source, target, actual.SourceCut, token, expectedPlacement), actual);
        var current = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(1, seed.Partition), cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(1, seed.Partition,
            target.PhysicalShardId, target.Incarnation, target.VoterIds, expectedPlacement.PlacementEpoch,
            original.DirectoryRevision, expectedPlacement.Revision, false), current);
    }
}
