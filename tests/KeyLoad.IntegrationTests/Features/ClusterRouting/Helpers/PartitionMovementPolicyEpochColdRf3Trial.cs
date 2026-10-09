using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPolicyEpochColdRf3Trial
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
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var first = await PartitionMovementPolicyEpochColdRf3Operations.TransferAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var original = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Assertions.RequirePolicyAsync(original,
            PartitionMovementPublicParentRf3Administrator.InitialDefinition(), first);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Operations.DemoteAsync(wave, cancellationToken).ConfigureAwait(false);
        await RequireDemotedAsync(wave, seed, first, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Operations.RestoreAsync(wave, cancellationToken).ConfigureAwait(false);
        var restored = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Assertions.RequireRestoredAsync(original, restored, first);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            first, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Replays.RequireOldUsersDeniedAsync(seed, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Assertions.RequireReplayRowsAsync(wave, seed, restored, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await ReverseAndColdAsync(wave, seed, first, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireDemotedAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult first, CancellationToken cancellationToken)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPolicyEpochColdRf3Assertions.RequireDemotedAsync(before, first);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        foreach (var mode in new[] { PartitionMoveMode.Resume, PartitionMoveMode.Abort })
        {
            await PartitionMovementPublicCallerReplay.RequireDeniedSubjectAsync(seed.Source, seed.Official,
                seed.FirstRequest with { Mode = mode }, cancellationToken).ConfigureAwait(false);
        }
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReverseAndColdAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult first, CancellationToken cancellationToken)
    {
        var placement = await PartitionMovementPolicyEpochColdRf3Operations.PlacementAsync(seed,
            cancellationToken).ConfigureAwait(false);
        var request = seed.FirstRequest with
        {
            MoveId = Guid.NewGuid(),
            DestinationPhysicalShardId = first.SourceOwner.PhysicalShardId,
            ExpectedPlacementRevision = placement.Revision,
            Mode = PartitionMoveMode.Transfer
        };
        var terminal = await PartitionMovementPolicyEpochColdRf3Operations.TransferAsync(seed, request,
            placement, first.DestinationOwner, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, cancellationToken).ConfigureAwait(false);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, request,
            terminal, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, request,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }
}
