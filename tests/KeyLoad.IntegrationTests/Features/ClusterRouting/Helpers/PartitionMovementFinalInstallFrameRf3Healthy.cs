using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFinalInstallFrameRf3Healthy
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMoveResult terminal, DateTimeOffset freshStart,
        int exactCap, CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, terminal, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        foreach (var control in before.Where(cut => cut.Header is not null))
        {
            var original = control.OriginalPhase!;
            await Assert.That(original.OriginalGrant!.GrantId).IsEqualTo(precut.GrantId);
            await Assert.That(original.OriginalGrant.ExpiresAt).IsGreaterThan(freshStart);
            await Assert.That(original.OriginalResult!.Error).IsNull();
            await Assert.That(original.ObservationCheckpointReceipt).IsNotNull();
        }
        var frames = await PartitionMovementFinalInstallFrameRf3Trial.ReadFinalFramesAsync(wave, seed, precut,
            before, exactCap, cancellationToken);
        foreach (var frame in frames)
        {
            await Assert.That(frame.Installed).IsTrue();
            await Assert.That(frame.PayloadBytes).IsEqualTo(exactCap);
        }
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            terminal, cancellationToken);
        await seed.VerifyAsync(cancellationToken);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        foreach (var original in before.Where(cut => cut.Header is not null))
        { await SqlRf3Protocol.EqualAsync(original.OriginalPhase, after.Single(cut => cut.Node == original.Node).OriginalPhase); }
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
    }
}
