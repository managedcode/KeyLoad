using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

internal static class NativeReadCutStoreStateAssertions
{
    internal static StoreIdentity SnapshotIdentity(ZoneTreeStore store)
    {
        var identity = store.Identity;
        return identity with { SigningKey = identity.SigningKey.ToArray() };
    }

    internal static async Task AssertIdentityAndPositionAsync(ZoneTreeStore store, StoreIdentity expected, long position)
    {
        var actual = store.Identity;
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
    }

    internal static async Task AssertRecordAsync(ZoneTreeStore store, byte[] key, byte[] expectedValue)
    {
        var actual = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.SequenceEqual(expectedValue)).IsTrue();
    }
}
