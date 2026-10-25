using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnProviderQualificationImages
{
    internal static (StoreIdentity Identity, long Position, string[] Rows,
        (long AppliedPosition, long StorePosition) CanonicalCut) Capture(AnnProviderQualificationOwner owner)
        => (ReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store), owner.ReadCanonicalCut());

    internal static async Task SameAsync(AnnProviderQualificationOwner owner,
        (StoreIdentity Identity, long Position, string[] Rows,
        (long AppliedPosition, long StorePosition) CanonicalCut) expected)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store)).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
        await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(expected.CanonicalCut);
    }
}
