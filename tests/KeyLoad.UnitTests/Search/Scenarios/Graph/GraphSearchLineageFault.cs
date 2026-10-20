using KeyLoad.Server;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.Search;

internal static class GraphSearchLineageFault
{
    internal static async Task RunAsync(GraphSearchLineageFixture state)
    {
        var key = KeySpace.Partition(GraphSearchLineageProtocol.DocumentFamily, state.Partition,
            GraphSearchLineageProtocol.Collection, GraphSearchLineageProtocol.Alpha);
        var original = state.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(GraphSearchLineageProtocol.Missing);
        var damaged = OrleansSerialization.Serialize(GraphSearchLineageProtocol.WrongRow);
        string[]? restored = null;
        var position = 0L;
        byte[] identity = [];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Write(state, key, damaged);
            await GraphSearchLineageValues.RefusedAsync(state, ErrorCode.PermissionDenied, GraphSearchLineageProtocol.Denied);
            await state.PolicyAsync(GraphSearchLineageProtocol.UseEpoch, true);
            await GraphSearchLineageValues.RefusedAsync(state, ErrorCode.Corruption, null);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var image = CanonicalStoreImage.Bytes(state.Store);
            var damagedRow = Convert.ToHexString(key) + ":" + Convert.ToHexString(damaged);
            var originalRow = Convert.ToHexString(key) + ":" + Convert.ToHexString(original);
            restored = image.Select(row => row == damagedRow ? originalRow : row).ToArray();
            position = state.Store.Position + 1L;
            identity = OrleansSerialization.Serialize(state.Store.Identity);
            await Assert.That(image.Count(row => row == damagedRow)).IsEqualTo(1);
        }, failures);
        ServerFailureObserver.Observe(() => Write(state, key, original), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(restored).IsNotNull();
            await GraphLabelUseOperations.ConserveAsync(state.Store, restored!, position, identity);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static void Write(GraphSearchLineageFixture state, byte[] key, byte[] value)
        => state.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });
}
