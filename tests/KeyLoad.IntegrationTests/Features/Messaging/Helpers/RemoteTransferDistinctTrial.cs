using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctTrial
{
    internal static async Task RunAsync()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var capture = new RemoteTransferStartupCapture();
            await RunOwnedAsync(capture, failures, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(RemoteTransferStartupCapture capture, List<Exception> failures,
        CancellationToken token)
    {
        LocalRf3ImageTestSession? localImage = null;
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? parent = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false);
            wave = await RemoteTransferRf3Enrollment.StartAsync(RemoteTransferDistinctProtocol.TechnicalSubject,
                localImage?.Selection, capture.Callbacks, token);
            parent = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
            var setup = await RemoteTransferDistinctSetup.CreateAsync(wave, parent, token);
            var terminal = await McpCallerAssertions.SdkSuccessAsync(await parent.Source.MovePartitionAsync(
                parent.FirstRequest, token));
            await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(parent, parent.FirstRequest,
                parent.OriginalPlacement, parent.Directory.ControlOwner, terminal, token);
            var source = await McpCallerAssertions.SdkSuccessAsync(await parent.Source.ReadAtomicPartitionPlacementAsync(
                new(1, setup.Transfer.Scenario.SourcePartition), token));
            await Assert.That(source.PhysicalShardId).IsEqualTo(parent.Directory.ControlOwner.PhysicalShardId);
            await Assert.That(source.PhysicalShardId).IsNotEqualTo(terminal.DestinationOwner.PhysicalShardId);
            await RemoteTransferDistinctStages.ExecuteAsync(wave, setup, parent, token);
        }, failures);
        if (parent is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures); }
        var beforeWaveCleanup = failures.Count;
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures); }
        var waveCleanupSucceeded = wave is not null && failures.Count == beforeWaveCleanup;
        if (localImage is { } ownedImage)
        { await ServerFailureObserver.ObserveAsync(() => ownedImage.DisposeAsync(removeImage: waveCleanupSucceeded).AsTask(), failures); }
    }
}
