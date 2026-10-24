using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class ModelHeadAuthorityFault
{
    internal static async Task RunAsync(ModelHeadAuthorityFixture fixture, DatabaseEngine database,
        ZoneTreeStore store, int fault, CancellationToken token)
    {
        var key = fixture.HeadKey;
        var original = store.Read(view => view.ReadOwnedValue(key))!;
        await ModelHeadAuthorityOperations.EqualAsync(OrleansSerialization.Deserialize<StreamHead>(original), new(2L, 1L, 1L));
        var damaged = OrleansSerialization.Serialize(ModelHeadAuthorityRequests.Fault(fault));
        var image = CanonicalStoreImage.Bytes(store);
        var identity = OrleansSerialization.Serialize(store.Identity);
        var cut = store.Position;
        var prefix = Convert.ToHexString(key) + ":";
        var originalRow = prefix + Convert.ToHexString(original);
        await Assert.That(image.Count(row => row == originalRow)).IsEqualTo(1);
        var expected = image.Select(row => row == originalRow ? prefix + Convert.ToHexString(damaged) : row).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store.Commit((transaction, _) => { transaction.Put(key, damaged); return true; });
            await GraphLabelUseOperations.ConserveAsync(store, expected, cut + 1L, identity);
            await ModelHeadAuthorityOperations.RefuseAsync(database, store, fixture.Harness.Partition, ModelHeadAuthorityFixture.Denied,
                ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", 1L, token);
            await ModelHeadAuthorityOperations.RefuseAsync(database, store, fixture.Harness.Partition, ModelHeadAuthorityFixture.Reader,
                ErrorCode.Corruption, "The retained event source is inconsistent.", 1L, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
        {
            store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
            return Task.CompletedTask;
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, cut + 2L, identity), failures);
        await ServerFailureObserver.ObserveAsync(() => ModelHeadAuthorityOperations.HealthyAsync(fixture, database, store, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ModelHeadAuthorityOperations.ReplayAsync(fixture, database, store, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
