using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>One original actual captured cohort owns denial, exact restoration and healthy continuation.</summary>
internal static class PartitionMovementRetainedPageCapacityRf3Trial
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
            var originalLimit = new DatabaseLimits().MaxBatchBytes;
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(originalLimit, caller.Token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token);
            var blob = await PartitionMovementRetainedPageCapacityRf3BlobCorpus.CreateAsync(seed, caller.Token);
            await ExecuteAsync(wave, seed, blob, originalLimit, caller.Token);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementRetainedPageCapacityRf3BlobCorpus blob, int originalLimit, CancellationToken cancellationToken)
    {
        var precut = await PartitionMovementParentOperationalCapacityRf3Preparation.CaptureAsync(wave, seed,
            originalLimit, cancellationToken);
        var inspected = await PartitionMovementParentOperationalCapacityRf3Preparation.InspectPageAndRestoreAsync(
            wave, seed, precut, cancellationToken);
        var cap = await PartitionMovementRetainedPageCapacityRf3Bounds.RequireAsync(precut.State, precut.Cuts,
            seed, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await wave.ReconfigureMovementCapacityAsync(cap, cancellationToken);
            await PartitionMovementRetainedPageCapacityRf3Producer.RequireDeniedAsync(wave, seed, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRefusedAsync(seed, cancellationToken);
            var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, precut.Cuts, after);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                cancellationToken);
            await precut.Refused.RestoreAsync(wave, stopped, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
            await wave.ReconfigureMovementCapacityAsync(originalLimit, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireHealthyAsync(wave, seed, inspected,
                cancellationToken);
            await PartitionMovementRetainedPageCapacityRf3Assertions.RequireHealthyBlobAsync(wave, seed, blob,
                cancellationToken);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
