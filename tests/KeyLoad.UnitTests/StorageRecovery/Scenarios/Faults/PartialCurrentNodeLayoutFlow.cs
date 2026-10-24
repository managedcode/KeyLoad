using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartialCurrentNodeLayoutFlow
{
    private static readonly byte[] Key = KeyCodec.Encode("current-partial-layout", "healthy-write");
    private static readonly byte[] Value = "execution-current-prefix-write"u8.ToArray();
    private const string Separator = ":";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PartitionHostRecoveryFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(PartitionHostRecoveryFixture fixture)
    {
        var originalPrefix = await PartialCurrentNodeLayoutPrefix.CreateAsync(fixture);
        var failures = new List<Exception>();
        string[]? expected = null;
        string? identity = null;
        long position = 0;
        for (var cold = 0; cold <= 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RunHostAsync(async host =>
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var store = (ZoneTreeStore)host.Database.Store;
                    if (cold == 0)
                    {
                        var before = PartialCurrentNodeLayoutState.Rows(store);
                        var start = store.Position;
                        await Assert.That(store.Commit((transaction, _) =>
                        {
                            transaction.Put(Key, Value);
                            return true;
                        })).IsTrue();
                        expected = before.Append(Convert.ToHexString(Key) + Separator + Convert.ToHexString(Value))
                            .Order(StringComparer.Ordinal).ToArray();
                        position = checked(start + 1);
                        identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
                    }
                    await Assert.That(store.Position).IsEqualTo(position);
                    await Assert.That(PartialCurrentNodeLayoutState.Rows(store)).IsEquivalentTo(expected!, CollectionOrdering.Matching);
                    await Assert.That(store.Read(view => view.ReadOwnedValue(Key))).IsEquivalentTo(Value, CollectionOrdering.Matching);
                    await Assert.That(JsonSerializer.Serialize(store.Identity, JsonDefaults.Options)).IsEqualTo(identity);
                    await Assert.ThrowsExactlyAsync<IOException>(fixture.OpenAndDisposeHostAsync);
                    await Assert.That(store.Position).IsEqualTo(position);
                    await Assert.That(PartialCurrentNodeLayoutState.Rows(store)).IsEquivalentTo(expected!, CollectionOrdering.Matching);
                }, failures);
            }), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            await PartialCurrentNodeLayoutPrefix.RequireAsync(fixture, originalPrefix);
        }
    }
}
