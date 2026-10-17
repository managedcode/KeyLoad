using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Flow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            var seed = await ResourcePartitionInventoryRf3Corpus.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            var models = await ResourcePartitionInventoryModelsClosure.InitializeAsync(wave, seed, caller.Token).ConfigureAwait(false);
            var baseline = await ResourcePartitionInventoryRf3Warm.RunAsync(wave, seed, caller.Token).ConfigureAwait(false);
            await ResourcePartitionInventoryModelsClosure.VerifyAsync(wave, seed, models, caller.Token).ConfigureAwait(false);
            var movement = await ResourcePartitionInventoryRf3Move.RunAsync(wave, seed, baseline, caller.Token).ConfigureAwait(false);
            models = await ResourcePartitionInventoryModelsMovedCut.CaptureAsync(
                wave, seed, models, movement, caller.Token).ConfigureAwait(false);
            for (var cold = 0; cold < ResourcePartitionInventoryRf3Protocol.ColdCount; cold++)
            {
                await ColdAsync(wave, seed, movement, models, caller.Token).ConfigureAwait(false);
            }
            using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, caller.Token).ConfigureAwait(false);
            await ResourcePartitionInventoryRf3Budget.RunAsync(wave, seed, membership, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is { } resources)
        {
            if (failures.Count > 0)
            {
                resources.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        ResourcePartitionInventoryRf3MoveState movement, ResourcePartitionInventoryModelsState models, CancellationToken token)
    {
        Dictionary<string, string> prior;
        using (var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false))
        {
            prior = membership.Owners.ToDictionary(pair => pair.Key, pair => pair.Value.SiloAddress, StringComparer.Ordinal);
        }
        await ResourcePartitionInventoryRf3Cold.ReopenAsync(wave, token).ConfigureAwait(false);
        using var current = await RemoteTransferNativeMembership.ReadAsync(wave, token).ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Assert.That(current.Owners[node].SiloAddress).IsNotEqualTo(prior[node]);
        }
        await ResourcePartitionInventoryRf3PolicyReplay.VerifyAsync(wave, seed, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3PublicAssertions.LateFailureAsync(wave,
            seed with
            {
                LateFailure = seed.LateFailure with
                {
                    CommandId = Guid.NewGuid()
                }
            }, token).ConfigureAwait(false);
        await ResourcePartitionInventoryRf3MoveReplay.VerifyAsync(wave, seed, movement, token).ConfigureAwait(false);
        await ResourcePartitionInventoryModelsClosure.VerifyAsync(wave, seed, models, token).ConfigureAwait(false);
    }
}
