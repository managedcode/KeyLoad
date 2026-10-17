using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class ControlledBlobLifetimeRf3Trial
{
    private const long InitialChangedEpoch = 2;
    private const long NextEpoch = 1;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is { } owned && failures.Count != 0)
        {
            owned.RetainRoots();
        }
        if (seed is { } clients)
        {
            await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (wave is { } resources)
        {
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var terminal = await PartitionMovementPolicyEpochColdRf3Operations.TransferAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        var epoch = InitialChangedEpoch;
        foreach (var kind in ControlledBlobLifetimeReadCalls.Kinds)
        {
            var changed = epoch;
            await ControlledBlobLifetimeHeldOperation.RunAsync(wave, seed, kind,
                (_, _) => ConfigureAsync(wave, changed, false, token), ErrorCode.PermissionDenied, token)
                .ConfigureAwait(false);
            epoch += NextEpoch;
            await ConfigureAsync(wave, epoch, true, token).ConfigureAwait(false);
            epoch += NextEpoch;
            await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        }
        await ControlledBlobLifetimeHeldOperation.RunAsync(wave, seed, GrainReadKind.BlobRange,
            (_, operation) => operation.CancelAsync(), ErrorCode.Cancelled, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        await ControlledBlobLifetimeHeldOperation.RunAsync(wave, seed, GrainReadKind.BlobMetadata,
            (expiry, _) => WaitForExpiryAsync(expiry, token), null, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        await ColdAsync(wave, seed, terminal, token).ConfigureAwait(false);
    }

    private static async Task WaitForExpiryAsync(DateTimeOffset expiry, CancellationToken token)
    {
        var remaining = expiry - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, TimeProvider.System, token).ConfigureAwait(false);
        }
        await Assert.That(TimeProvider.System.GetUtcNow() >= expiry).IsTrue();
    }

    private static async Task ConfigureAsync(TwoRf3MembershipWave wave, long epoch, bool administrator,
        CancellationToken token)
    {
        var expected = PartitionMovementPublicParentRf3Administrator.Definition(epoch, administrator);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            using var http = McpCallerHttp.Create(wave.Application, node);
            var root = new KeyLoadClient(http, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token).ConfigureAwait(false)));
        }
    }

    private static async Task ColdAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, CancellationToken token)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token)
            .ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await seed.ReconnectAsync(wave, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token)
            .ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await seed.ReconnectAsync(wave, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
        var second = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token)
            .ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(after, second);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await seed.ReconnectAsync(wave, token).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            terminal, token).ConfigureAwait(false);
        await PartitionMovementBlobCurrentReadsRf3Assertions.RequireAsync(seed, token).ConfigureAwait(false);
    }
}
