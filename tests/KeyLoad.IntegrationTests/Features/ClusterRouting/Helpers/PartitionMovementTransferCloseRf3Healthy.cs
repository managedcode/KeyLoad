using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The original Abort replays over all public routes before a fresh move and true cold healthy receipts.</summary>
internal static class PartitionMovementTransferCloseRf3Healthy
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult aborted, CancellationToken cancellationToken)
    {
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            aborted, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var fresh = seed.FirstRequest with { MoveId = Guid.NewGuid(), Mode = PartitionMoveMode.Transfer };
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(fresh,
            cancellationToken).ConfigureAwait(false));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, fresh, seed.OriginalPlacement,
            seed.Directory.ControlOwner, healthy, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, fresh,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, fresh, healthy,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, fresh,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }
}
