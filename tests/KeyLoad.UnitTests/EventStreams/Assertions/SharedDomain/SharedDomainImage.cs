using KeyLoad.RecoveryTests.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainImage
{
    internal static string[] Capture(IAtomicStore store, PartitionRef? partition = null)
        => SharedDomainStoredImage.Capture(store, partition);

    internal static async Task SameAsync(IAtomicStore store, string[] before, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(Capture(store)).IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task ScopeAsync(IAtomicStore store, PartitionRef partition, string[] before)
        => await Assert.That(Capture(store, partition)).IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);

    internal static DateTimeOffset Clock(IAtomicStore store)
        => OrleansSerialization.Deserialize<DateTimeOffset>(Required(store, KeySpace.Clock.ToArray()));

    internal static byte[] Required(IAtomicStore store, byte[] key)
        => store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(RuntimeMissing);

    internal static async Task BytesAsync(IAtomicStore store, byte[] key, byte[] expected)
        => await Assert.That(Required(store, key).AsSpan().SequenceEqual(expected)).IsTrue();
}
