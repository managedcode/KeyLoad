using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupMatrixRf3Trial
{
    internal static async Task RunAsync(PartitionMovementCleanupMatrixFaultRole role, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, role, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > PartitionMovementCleanupMatrixProtocol.EmptyFailureLedger)
        { wave?.RetainRoots(); }
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } owners)
        { await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementCleanupMatrixFaultRole role, CancellationToken cancellationToken)
    {
        var initial = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        var original = wave.QueryControls;
        var faults = role == PartitionMovementCleanupMatrixFaultRole.RetiredOtherResurrection
            ? await PartitionMovementCleanupMatrixRetiredRf3Producer.ExecuteAsync(wave, seed, cancellationToken).ConfigureAwait(false)
            : await PartitionMovementCleanupMatrixRf3Producer.ExecuteAsync(wave, seed, role, cancellationToken).ConfigureAwait(false);
        var failed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed), cancellationToken).ConfigureAwait(false);
        await RequireCutAsync(seed, initial, failed, role);
        await PartitionMovementCleanupMatrixRf3Session.RenewAsync(wave, original, faults,
            cancellationToken).ConfigureAwait(false);
        var restored = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed), cancellationToken).ConfigureAwait(false);
        await RequireCutAsync(seed, failed, restored, role);
        await PartitionMovementActiveAdjunctRf3Healthy.RequireAsync(wave, seed, restored,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task RequireCutAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after,
        PartitionMovementCleanupMatrixFaultRole role)
        => role == PartitionMovementCleanupMatrixFaultRole.RetiredOtherResurrection
            ? PartitionMovementCleanupMatrixRetiredRf3Cut.RequireAsync(seed, before, after)
            : PartitionMovementActiveAdjunctRf3Assertions.RequireBusinessUnchangedAsync(seed, before, after);

}
