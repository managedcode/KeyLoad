using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.ResourceExecution;

internal static class SearchReadBudgetCold
{
    internal static async Task RunAsync(TestDatabase fixture, string[] image, long position,
        Func<ZoneTreeStore, Task> operation, List<Exception> failures)
    {
        var identity = fixture.Store.Identity;
        var originalIdentity = System.Text.Json.JsonSerializer.Serialize(identity, JsonDefaults.Options);
        fixture.Store.Dispose();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(cold.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(cold.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
                var coldIdentity = System.Text.Json.JsonSerializer.Serialize(cold.Identity, JsonDefaults.Options);
                await Assert.That(coldIdentity.AsSpan().SequenceEqual(originalIdentity)).IsTrue();
                await SearchReadBudgetAssertions.UnchangedAsync(cold, image, position, coldIdentity);
                await operation(cold);
                await SearchReadBudgetAssertions.UnchangedAsync(cold, image, position, coldIdentity);
            }, failures);
        }, failures);
    }
}
