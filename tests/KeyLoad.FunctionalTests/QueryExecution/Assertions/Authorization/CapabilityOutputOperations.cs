using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class CapabilityOutputOperations
{
    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(SqlInnerJoinRetainedIdentityFlow.Image(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
