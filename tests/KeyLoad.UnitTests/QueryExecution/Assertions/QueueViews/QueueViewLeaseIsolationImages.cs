using KeyLoad.UnitTests.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationImages
{
    internal static QueueViewLeaseIsolationImage Capture(DocumentPatchAtomicityOwner owner)
    {
        var cut = owner.ReadCut();
        return new(ReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store), cut.AppliedPosition, cut.StorePosition);
    }

    internal static async Task SameAsync(DocumentPatchAtomicityOwner owner, QueueViewLeaseIsolationImage expected)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store).SequenceEqual(expected.Rows)).IsTrue();
        var cut = owner.ReadCut();
        await Assert.That(cut.AppliedPosition).IsEqualTo(expected.AppliedPosition);
        await Assert.That(cut.StorePosition).IsEqualTo(expected.StorePosition);
    }

    internal static void RequireRoots(DocumentPatchAtomicityOwner owner)
    {
        foreach (var directory in new[] { owner.Source.Directory })
        {
            if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)
                || !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.WrongRoot);
            }
        }
    }
}
