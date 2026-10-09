using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>One original six-owner pre-admission cohort, exact one-byte refusal and same-move legal continuation.</summary>
internal static class PartitionMovementParentOperationalCapacityRf3Trial
{
    private const int BoundaryStep = 1;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var initial = new DatabaseLimits().MaxBatchBytes;
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(initial, caller.Token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token);
            await ExecuteAsync(wave, seed, initial, caller.Token);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        int initialLimit, CancellationToken cancellationToken)
    {
        var precut = await PartitionMovementParentOperationalCapacityRf3Preparation.CaptureAsync(wave, seed,
            initialLimit, cancellationToken);
        var inspected = await PartitionMovementParentOperationalCapacityRf3Preparation.InspectPageAndRestoreAsync(
            wave, seed, precut, cancellationToken);
        var cluster = PartitionMovementParentOperationalCapacityRf3Boundary.ClusterPrefix + wave.Profile.Incarnation.ToString("N");
        var minimum = await PartitionMovementParentOperationalCapacityRf3Boundary.RequireAsync(precut.State,
            inspected, cluster, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await wave.ReconfigureMovementCapacityAsync(checked(minimum - BoundaryStep), cancellationToken);
            _ = await PartitionMovementParentOperationalCapacityRf3Producer.ResumeAtPreflightAsync(wave, seed,
                ErrorCode.BudgetExceeded, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRefusedAsync(seed, cancellationToken);
            var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, precut.Cuts, after);
        }, failures).ConfigureAwait(false);
        // Original refusal/startup errors remain; restoration and healthy continuation are independently attempted.
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                cancellationToken);
            await precut.Refused.RestoreAsync(wave, stopped, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
            var actual = PartitionMovementParentCapacityRf3Snapshot.Read(wave,
                precut.Cuts.First(cut => cut.Header is not null).Node, seed, seed.FirstRequest, initialLimit,
                cancellationToken);
            var repeated = await PartitionMovementParentOperationalCapacityRf3Boundary.RequireAsync(actual,
                inspected, cluster, cancellationToken);
            await Assert.That(repeated).IsEqualTo(minimum);
            await wave.ReconfigureMovementCapacityAsync(minimum, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireHealthyAsync(wave, seed, inspected,
                cancellationToken);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
