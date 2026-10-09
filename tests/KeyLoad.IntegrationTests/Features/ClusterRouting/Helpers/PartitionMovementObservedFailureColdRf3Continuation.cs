using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Real failed native state survives restart, refusal, authorized Abort and a new complete move.</summary>
internal static class PartitionMovementObservedFailureColdRf3Continuation
{
    internal static Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut[] refused,
        int originalFrameCap, bool requirePolicyFence, CancellationToken cancellationToken)
        => RequireCoreAsync(wave, seed, precut, refused, originalFrameCap, requirePolicyFence, null, cancellationToken);

    internal static Task RequireRestoredCapacityAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut[] refused,
        int originalFrameCap, int originalBatchCap, CancellationToken cancellationToken)
        => RequireCoreAsync(wave, seed, precut, refused, originalFrameCap, false, originalBatchCap, cancellationToken);

    private static async Task RequireCoreAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, PartitionMovementPublicParentRf3NativeCut[] refused,
        int originalFrameCap, bool requirePolicyFence, int? originalBatchCap, CancellationToken cancellationToken)
    {
        wave.frameObservation!.RetireAfterJoinedStop(wave);
        if (originalBatchCap is { } batch)
        { await wave.ReconfigureMovementCapacityAsync(batch, cancellationToken).ConfigureAwait(false); }
        await wave.ReconfigureMovementFrameAsync(originalFrameCap, cancellationToken).ConfigureAwait(false);
        if (requirePolicyFence)
        {
            refused = await PartitionMovementPolicyBusyColdRf3Trial.RequireAsync(wave, seed, precut, refused,
                cancellationToken).ConfigureAwait(false);
        }
        var resume = await seed.Source.MovePartitionAsync(seed.FirstRequest with { Mode = PartitionMoveMode.Resume },
            cancellationToken).ConfigureAwait(false);
        await Assert.That(resume.IsFailed).IsTrue();
        await Assert.That(resume.Value).IsNull();
        await Assert.That(resume.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        var afterResume = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, refused, afterResume);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireRetainedAsync(refused, afterResume);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        var aborted = await PartitionMovementTransferCloseRf3AbortAssertions.AbortAsync(seed,
            cancellationToken).ConfigureAwait(false);
        var terminal = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireRetainedAsync(refused, terminal);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireDisposedAsync(refused, terminal, aborted);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementTransferCloseRf3Healthy.RequireAsync(wave, seed, aborted,
            cancellationToken).ConfigureAwait(false);
        var afterHealthy = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireRetainedAsync(refused, afterHealthy);
        await PartitionMovementObservedFailureColdRf3Assertions.RequireDisposedAsync(refused, afterHealthy, aborted);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }
}
