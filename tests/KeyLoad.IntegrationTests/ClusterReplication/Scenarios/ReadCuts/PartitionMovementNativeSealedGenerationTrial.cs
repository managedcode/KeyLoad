using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Two actual sealed receiver callbacks keep distinct cleanup identities and original public receipts.</summary>
internal static class PartitionMovementNativeSealedGenerationTrial
{
    private const long FirstCleanup = 0;
    private const long SecondCleanup = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var firstId = PhaseId(seed, FirstCleanup);
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, token);
        await PartitionMovementNativeSealedCallbacks.InterruptAsync(wave, seed, firstId, discovery, false, token);
        var firstCuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, firstId, token);
        var first = firstCuts.First(value => value.Pending is not null).Pending!;
        await RequirePendingAsync(first, firstId, FirstCleanup);
        await Assert.That(first.OriginalExpiresAt <= TimeProvider.System.GetUtcNow()).IsTrue();
        await PartitionMovementRetireGrantDispositionRf3Trial.RequirePendingAsync(firstCuts, first);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        var current = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, token);
        await PartitionMovementHistoricalPacketAssertions.RestartedAsync(discovery, current, first);
        var secondId = PhaseId(seed, SecondCleanup);
        await Assert.That(secondId).IsNotEqualTo(firstId);
        await PartitionMovementNativeSealedCallbacks.InterruptAsync(wave, seed, secondId, current, true, token);
        var secondCuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, firstId, token);
        var second = secondCuts.First(value => value.Pending is not null).Pending!;
        await RequirePendingAsync(second, secondId, SecondCleanup);
        foreach (var cut in secondCuts.Where(value => value.Header is not null))
        {
            await Assert.That(cut.Header!.TerminalResult).IsNull();
            await Assert.That(cut.Header.PendingOriginalPhaseCommandId).IsEqualTo((Guid?)secondId);
            await Assert.That(cut.RetireCancellation).IsNotNull();
            await Assert.That(cut.RetireCancellation!.OriginalPhaseCommandId).IsEqualTo(firstId);
            await Assert.That(cut.NativeCancellationResult).IsNotNull();
            await Assert.That(cut.NativeCancellationResult!.Error).IsNull();
            await PartitionMovementHistoricalPacketAssertions.ExactAsync(first, cut.OriginalPhase!);
        }
        var remaining = second.OriginalExpiresAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, token);
        }
        await Assert.That(second.OriginalExpiresAt <= TimeProvider.System.GetUtcNow()).IsTrue();
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.FirstRequest with
            {
                Mode = PartitionMoveMode.Resume
            }, token));
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await seed.VerifyAsync(token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest, terminal, token);
        var final = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, firstId, token);
        await PartitionMovementRetireGrantDispositionRf3Trial.RequireTerminalAsync(final, first);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest, terminal, token);
        await seed.VerifyAsync(token);
    }

    private static Guid PhaseId(PartitionMovementPublicParentRf3Seed seed, long generation)
        => PartitionMovementParentPhaseIds.For(seed.FirstRequest, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            PartitionMovementParentPhaseRole.Retire, cleanupGeneration: generation);

    private static async Task RequirePendingAsync(PartitionMoveParentPhase phase, Guid id, long generation)
    {
        await Assert.That(phase.OriginalPhaseCommandId).IsEqualTo(id);
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(phase.CleanupGeneration).IsEqualTo(generation);
        await Assert.That(phase.OriginalResult).IsNull();
        await Assert.That(phase.RetireCancellationAttempt).IsNull();
        await Assert.That(phase.OriginalReceiverIssuePacket).IsNotNull();
        await Assert.That(phase.OriginalAuthorization).IsNotNull();
        await Assert.That(phase.OriginalReceiverIssuanceWitness).IsNotNull();
    }
}
