using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
using P = KeyLoad.UnitTests.Features.StorageRecovery.CheckpointOriginalCauseProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CheckpointOriginalCauseAssertions
{
    internal static async Task CurrentAsync(CurrentStorageFixture files, ZoneTreeStore store, StoreIdentity identity)
    {
        await WalFileFixture.AssertPreservedIdentity(identity, store.Identity);
        await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(identity.MinimumReaderContract);
        await Assert.That(store.Position).IsEqualTo(P.Position);
        var page = store.Read(view => view.Scan([], P.ProbeRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(P.Records);
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Convert.ToHexString(files.FirstKey)] = files.FirstValue,
            [Convert.ToHexString(files.SecondKey)] = files.SecondValue
        };
        foreach (var row in page.Records)
        {
            var key = Convert.ToHexString(row.Key.Span);
            await Assert.That(expected.ContainsKey(key)).IsTrue();
            await Assert.That(row.Value.ToArray()).IsEquivalentTo(expected[key], CollectionOrdering.Matching);
            expected.Remove(key);
        }
        await Assert.That(expected).IsEmpty();
    }

    internal static async Task FailureAsync(CurrentStorageFixture files, ZoneTreeStore store,
        string mode, byte[] identity, byte[] journal)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => CheckpointOriginalCauseFlow.Execute(files, store, mode));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(failure.Message).IsEqualTo(P.Interrupted);
        await Assert.That(failure.InnerException is UnauthorizedAccessException).IsTrue();
        await Assert.That(failure.InnerException!.Message.Contains(P.Temporary, StringComparison.Ordinal)).IsTrue();
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(files.Source, P.Identity)))
            .IsEquivalentTo(identity, CollectionOrdering.Matching);
        await Assert.That(await WalFileFixture.ReadJournalAsync(Path.Combine(files.Source, P.Journal)))
            .IsEquivalentTo(journal, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(P.Position);
        var read = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(_ => true));
        await Assert.That(read.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(read.Message).IsEqualTo(P.Poisoned);
        var write = Assert.ThrowsExactly<KeyLoadException>(() => store.Commit((tx, _) =>
        {
            tx.Put(files.FirstKey, files.SecondValue);
            return true;
        }));
        await Assert.That(write.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(write.Message).IsEqualTo(P.Poisoned);
    }
}
