using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFencedAbortRf3Recovery
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;

    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests,
        RequestCqrsMovementJournalRecord proof, CancellationToken token)
    {
        var phase = PhysicalShardPartitionMovementRf3JournalAssertions.ReadPhase(wave, proof.Voter, proof)
            .Get<PartitionMovePhaseResult>();
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.ControlAcceptFence);
        await Assert.That(phase.Control!.Phase).IsEqualTo(PartitionMovePhase.Fenced);
        await wave.RestartJoinedAsync(token);
        await wave.WaitForSixHealthyAsync(token);
        foreach (var seed in seeds)
        { await seed.ReconnectAsync(wave, token); }
        var first = seeds[FirstEntry];
        var abort = requests[FirstEntry] with { Mode = PartitionMoveMode.Abort };
        var source = first.Directory.Owners.Single(owner =>
            owner.Owner.PhysicalShardId == first.OriginalPlacement.PhysicalShardId).Owner;
        var target = first.Directory.Owners.Single(owner =>
            owner.Owner.PhysicalShardId == abort.DestinationPhysicalShardId).Owner;
        var expected = new PartitionMoveResult(abort.MoveId, first.Partition, PartitionMovePhase.Aborted,
            source, target, phase.Control.SourceCut, null, null);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await first.Source.MovePartitionAsync(abort, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await PartitionMovementPublicCallerReplay.RequireAsync(first.Source, first.Official, abort, expected, token);
        await PartitionMovementFencedAbortRf3Assertions.UnchangedAsync(seeds, token);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, abort, token);
        await PartitionMovementFencedAbortRf3Assertions.TerminalAsync(before, expected);
        await PartitionMovementFencedAbortRf3Assertions.SecondaryAsync(wave, requests[SecondEntry]);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(first.Source, first.Official, abort, expected, token);
        await PartitionMovementFencedAbortRf3Assertions.UnchangedAsync(seeds, token);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, abort, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementFencedAbortRf3Assertions.SecondaryAsync(wave, requests[SecondEntry]);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        var fresh = requests.Select(request => request with { MoveId = Guid.NewGuid() }).ToArray();
        await PhysicalShardPartitionMovementRf3FaultOwner.ResumeAsync(wave, seeds, fresh, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(first.Source, first.Official, abort, expected, token);
    }
}
