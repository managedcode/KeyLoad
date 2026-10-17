using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphLabelUseFault
{
    internal static async Task RunAsync(TestDatabase db, CancellationToken token)
    {
        var key = KeySpace.Partition("document", db.Partition, GraphLabelUseFixture.Collection, "a");
        var original = db.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original canonical graph vertex must exist.");
        var image = GraphShortestPathFullStoreImage.Bytes(db.Store);
        var position = db.Store.Position;
        var identity = NativeSerialization.Serialize(db.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var damaged = NativeSerialization.Serialize("wrong-native-vertex-type");
            Write(db, key, damaged);
            var encoded = Convert.ToHexString(key) + ":";
            var originalRow = encoded + Convert.ToHexString(original);
            await Assert.That(image.Count(row => row == originalRow)).IsEqualTo(1);
            var expected = image.Select(row => row == originalRow ? encoded + Convert.ToHexString(damaged) : row).ToArray();
            await GraphLabelUseOperations.ConserveAsync(db.Store, expected, position + 1L, identity);
            await GraphLabelUseOperations.ObserveAsync(db.Store, () => GraphLabelUseOperations.RefuseAsync(() =>
                GraphLabelUseFixture.Traverse(db.Database, db.Partition, ["route"], token),
                ErrorCode.PermissionDenied, GraphLabelUseFixture.DeniedDetail));
            await GraphLabelUseOperations.ObserveAsync(db.Store, () => GraphLabelUseOperations.RefuseAsync(() =>
                GraphLabelUseFixture.Traverse(db.Database, db.Partition, null, token), ErrorCode.Corruption, null));
        }, failures);
        ServerFailureObserver.Observe(() => Write(db, key, original), failures);
        await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(db.Store,
            image, position + 2L, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await GraphLabelUseOperations.ReadAsync(db.Database, db.Store, db.Partition, false, token);
    }
    private static void Write(TestDatabase db, byte[] key, byte[] value)
        => db.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });
}
