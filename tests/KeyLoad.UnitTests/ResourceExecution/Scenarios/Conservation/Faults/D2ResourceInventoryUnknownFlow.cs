using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryUnknownFlow
{
    internal static async Task ExecuteAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new D2ResourceInventoryFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = await D2ResourceInventoryUnknownSeed.CreateAsync(fixture, token);
                await D2ResourceInventoryUnknownAssertions.RefusedAsync(fixture, token);
                var followup = D2ResourceInventoryUnknownSeed.Write(fixture,
                    D2ResourceInventoryUnknownSeed.FollowupId, D2ResourceInventoryData.Json[1], token);
                (ReplicatedOperation Operation, OperationResult Outcome)[] writes = [original, followup];
                await D2ResourceInventoryUnknownAssertions.ReplayAsync(fixture, writes, token);
                await D2ResourceInventoryUnknownAssertions.RefusedAsync(fixture, token);
                var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
                for (var pass = 0; pass < D2ResourceInventoryData.TwoColdOwners; pass++)
                {
                    fixture.Owner.Reopen();
                    await before.AssertUnchangedAsync(fixture.Owner);
                    await D2ResourceInventoryUnknownAssertions.RefusedAsync(fixture, token);
                    await D2ResourceInventoryUnknownAssertions.ReplayAsync(fixture, writes, token);
                    await before.AssertUnchangedAsync(fixture.Owner);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
