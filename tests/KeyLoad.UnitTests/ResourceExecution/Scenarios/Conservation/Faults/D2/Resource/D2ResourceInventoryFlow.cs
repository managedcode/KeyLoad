using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryFlow
{
    internal static async Task ExecuteAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new D2ResourceInventoryFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = await D2ResourceInventorySeeding.CreateAsync(fixture, token);
                await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.A, token);
                await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.B, token);
                await D2ResourceInventoryAssertions.EmptyAsync(fixture, seed, token);
                await D2ResourceInventoryRefusals.ExecuteAsync(fixture, seed, token);
                await D2ResourceInventoryPolicy.ExecuteAsync(fixture, seed, token);
                await D2ResourceInventoryCold.ExecuteAsync(fixture, seed, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
