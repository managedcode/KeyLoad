using KeyLoad.UnitTests.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationImages
{
    internal static QueueViewLeaseIsolationImage Capture(DocumentPatchAtomicityOwner owner)
    {
        var cut = owner.JournalCut();
        return new(NativeReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store), cut.LastIndex, cut.CommittedIndex, cut.Term);
    }

    internal static async Task SameAsync(DocumentPatchAtomicityOwner owner, QueueViewLeaseIsolationImage expected)
    {
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store).SequenceEqual(expected.Rows)).IsTrue();
        var cut = owner.JournalCut();
        await Assert.That(cut.LastIndex).IsEqualTo(expected.LastIndex);
        await Assert.That(cut.CommittedIndex).IsEqualTo(expected.CommittedIndex);
        await Assert.That(cut.Term).IsEqualTo(expected.Term);
    }

    internal static void RequireRoots(DocumentPatchAtomicityOwner owner)
    {
        foreach (var directory in new[] { owner.Source.Directory, owner.Source.Directory + TestDatabaseReplicaDirectory.Suffix })
        {
            if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)
                || !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.WrongRoot);
            }
        }
    }
}
