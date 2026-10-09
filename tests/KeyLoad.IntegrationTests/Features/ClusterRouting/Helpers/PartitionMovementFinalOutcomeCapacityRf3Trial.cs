using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFinalOutcomeCapacityRf3Trial
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var originalFrame = new ZoneTreeStorageExecutionOptions().MaxFrameBytes;
            wave = await TwoRf3MembershipWave.StartProtectedFramesAsync(originalFrame, caller.Token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token);
            await ExecuteAsync(wave, seed, originalFrame, caller.Token);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        int originalFrame, CancellationToken cancellationToken)
    {
        var precut = await PartitionMovementFinalInstallFrameRf3Preparation.CaptureAsync(wave, seed, cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        _ = await PartitionMovementFinalInstallFrameRf3Producer.ResumeFinalAsync(wave, seed, null, cancellationToken);
        var calibration = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        var exact = await PartitionMovementFinalOutcomeCapacityRf3Stages.RequireCalibrationAsync(wave, seed,
            precut, calibration, originalFrame, cancellationToken);
        await precut.Calibrated.RestoreAsync(wave, calibration, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
        var cluster = PartitionMovementParentOperationalCapacityRf3Boundary.ClusterPrefix + wave.Profile.Incarnation.ToString("N");
        var minimum = await PartitionMovementFinalOutcomeCapacityRf3Boundary.RequireAsync(precut, cluster, cancellationToken);
        await PartitionMovementFinalInstallFrameRf3Trial.RequireHistoryAsync(wave, seed, precut, originalFrame,
            checked(exact - PartitionMovementFinalOutcomeCapacityRf3Boundary.OneByte), cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => PartitionMovementFinalOutcomeCapacityRf3Stages.RefuseAsync(
            wave, seed, precut, minimum, exact, cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                precut.EffectId, cancellationToken);
            await precut.Refused.RestoreAsync(wave, stopped, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
            var actual = PartitionMovementParentCapacityRf3Snapshot.Read(wave,
                precut.Cuts.First(cut => cut.Header is not null).Node, seed, seed.FirstRequest,
                new DatabaseLimits().MaxBatchBytes, cancellationToken);
            var restored = precut with { State = actual };
            await Assert.That(await PartitionMovementFinalOutcomeCapacityRf3Boundary.RequireAsync(restored,
                cluster, cancellationToken)).IsEqualTo(minimum);
            await PartitionMovementFinalOutcomeCapacityRf3Stages.LegalThenHealthyAsync(wave, seed, restored,
                minimum, exact, originalFrame, cancellationToken);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
