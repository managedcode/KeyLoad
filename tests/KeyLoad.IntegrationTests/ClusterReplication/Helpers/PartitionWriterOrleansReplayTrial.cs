using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>The actual public movement owns provenance; only joined original disks enter the retained writer check.</summary>
internal static class PartitionWriterOrleansReplayTrial
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.FirstRequest, cancellationToken).ConfigureAwait(false));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, terminal, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        _ = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        var before = PartitionWriterOrleansColdImages.Capture(wave);
        var original = await PartitionWriterOrleansRetainedEntry.ReadAsync(wave, seed, cancellationToken);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(before, PartitionWriterOrleansColdImages.Capture(wave));
        var changedIdentity = Guid.NewGuid();
        await RefuseAsync(wave, seed, original, changedIdentity, before, cancellationToken);
        await RefuseAsync(wave, seed, original, changedIdentity, before, cancellationToken);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(before, PartitionWriterOrleansColdImages.Capture(wave));
        await RefuseAsync(wave, seed, original, changedIdentity, before, cancellationToken);
        await PartitionWriterOrleansNativeAppend.AppendAsync(wave, seed, original, cancellationToken);
        var restored = PartitionWriterOrleansColdImages.Capture(wave);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(before, restored, original.Node);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(restored, PartitionWriterOrleansColdImages.Capture(wave));
        await RestartAndReplayAsync(wave, seed, terminal, cancellationToken);
        var healthy = new PartitionWriterOrleansHealthyContinuation(seed);
        await healthy.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        await healthy.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        _ = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        var secondCold = PartitionWriterOrleansColdImages.Capture(wave);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(secondCold, PartitionWriterOrleansColdImages.Capture(wave));
        await RestartAndReplayAsync(wave, seed, terminal, cancellationToken);
        await healthy.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RefuseAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionWriterOrleansOriginal original, Guid changedIdentity, PartitionWriterOrleansImage[] before,
        CancellationToken cancellationToken)
    {
        await PartitionWriterOrleansNativeAppend.RefuseAsync(wave, seed, original, changedIdentity,
            cancellationToken).ConfigureAwait(false);
        await PartitionWriterOrleansNativeAssertions.ImagesAsync(before, PartitionWriterOrleansColdImages.Capture(wave));
    }

    private static async Task RestartAndReplayAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal, CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            terminal, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
    }
}
