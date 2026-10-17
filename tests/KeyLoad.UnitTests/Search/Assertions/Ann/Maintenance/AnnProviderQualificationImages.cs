using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationImages
{
    internal static (StoreIdentity Identity, long Position, string[] Rows,
        (long LastIndex, long CommittedIndex, long Term) Journal, Guid JournalNodeId) Capture(AnnProviderQualificationOwner owner)
        => (NativeReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store), owner.Native.ReadJournalCut(), owner.Native.JournalNodeId);

    internal static async Task SameAsync(AnnProviderQualificationOwner owner,
        (StoreIdentity Identity, long Position, string[] Rows,
        (long LastIndex, long CommittedIndex, long Term) Journal, Guid JournalNodeId) expected)
    {
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store)).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
        await Assert.That(owner.Native.ReadJournalCut()).IsEqualTo(expected.Journal);
        await Assert.That(owner.Native.JournalNodeId).IsEqualTo(expected.JournalNodeId);
        await Assert.That(owner.Native.JournalNodeId).IsEqualTo(owner.OriginalJournalNodeId);
    }
}
