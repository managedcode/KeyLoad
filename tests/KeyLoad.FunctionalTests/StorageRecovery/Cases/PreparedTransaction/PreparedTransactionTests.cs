using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PreparedTransactionTests
{
    private const string TemporaryDirectoryPrefix = "keyload-prepared-transaction-";
    private const int AllRecords = 8;

    [Test]
    public async Task AcPsw002ReplacementDeletionResetAndCallerBuffersKeepFinalMutations()
    {
        var root = CreateTemporaryDirectory();
        byte[] discardedKey = [0x10];
        byte[] discardedValue = [0x20];
        byte[] replacedKey = [0x30];
        byte[] finalValue = [0x40, 0x41, 0x42];
        byte[] finalDeleteKey = [0x50];

        try
        {
            using (var store = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution()))
            {
                store.Commit((transaction, _) =>
                {
                    transaction.Put(finalDeleteKey, discardedValue);
                    return true;
                });
                store.Commit((transaction, _) =>
                {
                    transaction.Put(discardedKey, discardedValue);
                    transaction.Put(replacedKey, discardedValue);
                    transaction.Put(replacedKey, finalValue);
                    transaction.Delete(discardedKey);
                    transaction.Reset();
                    transaction.Put(replacedKey, finalValue);
                    transaction.Delete(finalDeleteKey);
                    replacedKey[0] = 0x60;
                    finalValue[0] = 0x61;
                    finalDeleteKey[0] = 0x62;
                    return true;
                });

                await Assert.That(store.Position).IsEqualTo(2L);
                await AssertFinalRecordsAsync(store);
            }

            using var reopened = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution());
            await Assert.That(reopened.Position).IsEqualTo(2L);
            await AssertFinalRecordsAsync(reopened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AcPsw003ResetToEmptyCommitsNothingAndDoesNotAdvancePosition()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var store = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution()))
            {
                store.Commit((transaction, _) =>
                {
                    transaction.Put([0x71], [0x72]);
                    transaction.Reset();
                    return true;
                });

                await Assert.That(store.Position).IsEqualTo(0L);
                await Assert.That(store.Read(view => view.Scan([], AllRecords)).Records).IsEmpty();
            }

            using var reopened = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution());
            await Assert.That(reopened.Position).IsEqualTo(0L);
            await Assert.That(reopened.Read(view => view.Scan([], AllRecords)).Records).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertFinalRecordsAsync(ZoneTreeStore store)
    {
        var page = store.Read(view => view.Scan([], AllRecords));
        var rows = page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":"
            + Convert.ToHexString(record.Value.Span)).ToArray();
        string[] expected =
        [
            "30:404142",
            Convert.ToHexString(StoreFileFixture.PositionKey) + ":"
                + Convert.ToHexString(StoreFileFixture.PositionValue(store.Position))
        ];
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, TemporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
