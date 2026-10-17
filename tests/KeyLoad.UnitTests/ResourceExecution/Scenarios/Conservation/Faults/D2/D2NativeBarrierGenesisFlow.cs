using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2NativeBarrierGenesisFlow
{
    internal static async Task RunAsync(bool secondAuthentication, bool firstCommand, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new D2ResourceInventoryFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var proven = !secondAuthentication && !firstCommand;
                var lifetime = await D2NativeBarrierGenesisBootstrap.RunAsync(fixture,
                    secondAuthentication, firstCommand, token);
                var original = D2ResourceInventoryUnknownSeed.Write(fixture,
                    D2ResourceInventoryData.DocumentId, D2ResourceInventoryData.Json[D2ResourceInventoryData.EmptyRevision], token);
                await D2NativeBarrierGenesisAssertions.InventoryAsync(fixture, lifetime, proven, token);
                var healthy = D2ResourceInventoryUnknownSeed.Write(fixture,
                    D2ResourceInventoryUnknownSeed.FollowupId, D2ResourceInventoryData.Json[D2ResourceInventoryData.InitialRevision], token);
                (ReplicatedOperation Operation, OperationResult Outcome)[] writes = [original, healthy];
                await D2NativeBarrierGenesisAssertions.ReplayAsync(fixture, writes, token);
                await D2NativeBarrierGenesisAssertions.InventoryAsync(fixture, lifetime, proven, token);
                var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
                for (var pass = D2ResourceInventoryData.EmptyRevision; pass < D2ResourceInventoryData.TwoColdOwners; pass++)
                {
                    fixture.Owner.Reopen();
                    await before.AssertUnchangedAsync(fixture.Owner);
                    await D2NativeBarrierGenesisAssertions.InventoryAsync(fixture, lifetime, proven, token);
                    await D2NativeBarrierGenesisAssertions.ReplayAsync(fixture, writes, token);
                    await before.AssertUnchangedAsync(fixture.Owner);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
