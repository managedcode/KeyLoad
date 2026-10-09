using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFinalOutcomeCapacityRf3Stages
{
    internal static async Task<int> RequireCalibrationAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementFinalInstallFramePrecut precut,
        PartitionMovementPublicParentRf3NativeCut[] calibration, int originalFrame, CancellationToken cancellationToken)
    {
        var frames = await PartitionMovementFinalInstallFrameRf3Trial.ReadFinalFramesAsync(wave, seed, precut,
            calibration, originalFrame, cancellationToken);
        var exact = frames.First().PayloadBytes;
        foreach (var frame in frames)
        {
            await Assert.That(frame.Installed).IsTrue();
            await Assert.That(frame.PayloadBytes).IsEqualTo(exact);
            await Assert.That(frame.RawMutationBytes).IsLessThanOrEqualTo((long)exact - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte);
        }
        return exact;
    }

    internal static async Task RefuseAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, int minimum, int exact, CancellationToken cancellationToken)
    {
        await ConfigureAsync(wave, checked(minimum - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte),
            exact, cancellationToken);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        _ = await PartitionMovementFinalInstallFrameRf3Producer.ResumeCapacityRefusalAsync(wave, seed, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRefusedAsync(seed, cancellationToken);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        foreach (var cut in after)
        {
            await Assert.That(cut.OriginalPhase).IsNull();
            await Assert.That(cut.OriginalNativeResult).IsNull();
            await Assert.That(cut.OriginalReceiverIssuance).IsNull();
            if (cut.Header is not null)
            {
                await Assert.That(PartitionMovementParentCapacityRf3Snapshot.ReadGrant(wave, cut.Node,
                    seed.FirstRequest, precut.GrantId, checked(minimum - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte))).IsNull();
            }
        }
    }

    internal static async Task LegalThenHealthyAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, int minimum, int exact, int originalFrame,
        CancellationToken cancellationToken)
    {
        wave.frameObservation = MovementFrameObservationFixtureFactory.Create(wave, seed);
        await ConfigureAsync(wave, minimum, exact, cancellationToken);
        wave.frameObservation.Select(seed, MovementFrameObservationSelectionMode.Exact);
        var issuedAfter = TimeProvider.System.GetUtcNow();
        _ = await PartitionMovementFinalInstallFrameRf3Producer.ResumeFinalAsync(wave, seed,
            ErrorCode.ResourceExhausted, cancellationToken);
        var failed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        await PartitionMovementFinalInstallFrameRf3Assertions.RequireDeniedAsync(wave, seed, precut, failed);
        await MovementFrameObservationRf3Assertions.RequireAsync(wave, seed, precut, failed, exact);
        var frames = await PartitionMovementFinalInstallFrameRf3Trial.ReadFinalFramesAsync(wave, seed, precut,
            failed, checked(exact - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte), cancellationToken);
        foreach (var frame in frames)
        { await Assert.That(frame.EncodedFrameLimitRejected).IsTrue(); }
        foreach (var cut in failed)
        {
            if (cut.Header is not null)
            {
                await Assert.That(cut.OriginalPhase!.OriginalGrant!.ExpiresAt).IsGreaterThan(issuedAfter);
                await Assert.That(cut.OriginalPhase.ObservationCheckpointReceipt).IsNotNull();
                await Assert.That(ZoneTreeStore.IsEncodedFrameLimitRejection(cut.OriginalPhase.OriginalResult)).IsTrue();
            }
        }
        await PartitionMovementObservedFailureColdRf3Continuation.RequireRestoredCapacityAsync(wave, seed, precut, failed,
            originalFrame, new DatabaseLimits().MaxBatchBytes, cancellationToken);
    }

    private static async Task ConfigureAsync(TwoRf3MembershipWave wave, int batch, int exact,
        CancellationToken cancellationToken)
    {
        await wave.ReconfigureMovementFrameAsync(checked(exact - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte),
            cancellationToken);
        await wave.ReconfigureMovementCapacityAsync(batch, cancellationToken);
    }
}
