using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CurrentCheckpointRepairContinuation
{
    private const long HealthyIncrement = 1;
    private const int SourceRows = 2;
    private const int RestoredRows = 3;
    private const byte OriginalKey = 0x10;
    private const byte OriginalValue = 0x30;
    private const byte FollowupKey = 0x20;
    private const byte FollowupValue = 0x40;

    internal static async Task RequireAsync(WalFileFixture files, StoreIdentity originalIdentity,
        long originalPosition, byte[] originalJournal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(files.JournalPath, originalJournal, token).ConfigureAwait(false);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath, token))
            .IsEquivalentTo(originalJournal, CollectionOrdering.Matching);
        await RequireCurrentAsync(files, originalIdentity, originalPosition, token).ConfigureAwait(false);
    }

    internal static async Task RequireCurrentAsync(WalFileFixture files, StoreIdentity originalIdentity,
        long originalPosition, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using (var repaired = Open(files.DirectoryPath))
        {
            await RequireIdentityAsync(originalIdentity, repaired.Identity);
            await Assert.That(repaired.Position).IsEqualTo(originalPosition);
            await Assert.That(repaired.Read(view => view.ReadOwnedValue([OriginalKey]))!)
                .IsEquivalentTo(new byte[] { OriginalValue }, CollectionOrdering.Matching);
            await Assert.That(repaired.Read(view => view.ReadOwnedValue([FollowupKey]))).IsNull();
            repaired.Commit((transaction, position) =>
            {
                transaction.Put([FollowupKey], [FollowupValue]);
                return position;
            });
            await Assert.That(repaired.Position).IsEqualTo(originalPosition + HealthyIncrement);
        }
        using var destinations = new CurrentStorageFixture();
        var backupPosition = await ColdBackupAsync(files, destinations, originalIdentity, originalPosition);
        var backup = await CaptureAsync(destinations.Backup, token);
        var incarnation = Guid.NewGuid();
        var restoredIdentity = ZoneTreeStore.Restore(destinations.Backup, destinations.Restored, UnitExecutionOptions.StorageExecution(), incarnation, cancellationToken: token);
        await Assert.That(restoredIdentity.NodeId).IsNotEqualTo(originalIdentity.NodeId);
        await Assert.That(restoredIdentity.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restoredIdentity.SigningKey.Span.SequenceEqual(originalIdentity.SigningKey.Span)).IsFalse();
        await Assert.That(restoredIdentity.DispatchPaused).IsTrue();
        await RequireBackupUnchangedAsync(destinations.Backup, backup, token);
        await RequireRestoredAsync(destinations.Restored, restoredIdentity, backupPosition);
        await RequireRestoredAsync(destinations.Restored, restoredIdentity, backupPosition);
        await RequireSourceAfterRestoreAsync(files.DirectoryPath, originalIdentity, backupPosition);
    }

    private static ZoneTreeStore Open(string directory) => new(new(directory),
        UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static async Task<long> ColdBackupAsync(WalFileFixture files, CurrentStorageFixture destinations,
        StoreIdentity originalIdentity, long originalPosition)
    {
        using var cold = Open(files.DirectoryPath);
        await RequireIdentityAsync(originalIdentity, cold.Identity);
        await Assert.That(cold.Position).IsEqualTo(originalPosition + HealthyIncrement);
        await RequireRowsAsync(cold, restored: false);
        cold.SetDispatchPaused(true);
        await RequireIdentityAsync(originalIdentity with
        {
            DispatchPaused = true
        }, cold.Identity);
        var cut = cold.CreateBackup(destinations.Backup);
        await Assert.That(cut).IsEqualTo(originalPosition + HealthyIncrement);
        return cut;
    }

    private static async Task RequireRestoredAsync(string directory, StoreIdentity expected, long backupPosition)
    {
        using var cold = Open(directory);
        await RequireIdentityAsync(expected, cold.Identity);
        await Assert.That(cold.Position).IsEqualTo(backupPosition + HealthyIncrement);
        await RequireRowsAsync(cold, restored: true);
    }

    private static async Task RequireSourceAfterRestoreAsync(string directory, StoreIdentity originalIdentity,
        long position)
    {
        using var source = Open(directory);
        await RequireIdentityAsync(originalIdentity with
        {
            DispatchPaused = true
        }, source.Identity);
        await Assert.That(source.Position).IsEqualTo(position);
        await RequireRowsAsync(source, restored: false);
    }

    private static async Task RequireIdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await WalFileFixture.AssertPreservedIdentity(expected, actual);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
    }

    private static async Task RequireRowsAsync(ZoneTreeStore store, bool restored)
    {
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Convert.ToHexString(new byte[] { OriginalKey })] = [OriginalValue],
            [Convert.ToHexString(new byte[] { FollowupKey })] = [FollowupValue]
        };
        if (restored)
        {
            expected.Add(Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)), NativeSerialization.Serialize(true));
        }
        var page = store.Read(view => view.Scan([], RestoredRows + 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(restored ? RestoredRows : SourceRows);
        var actual = page.Records.ToDictionary(record => Convert.ToHexString(record.Key.Span), record => record.Value.ToArray());
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var row in expected)
        {
            await Assert.That(actual[row.Key]).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
        }
    }

    private static async Task<Dictionary<string, byte[]>> CaptureAsync(string directory, CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, file), await File.ReadAllBytesAsync(file, token));
        }
        return result;
    }

    private static async Task RequireBackupUnchangedAsync(string directory,
        Dictionary<string, byte[]> expected, CancellationToken token)
    {
        var actual = await CaptureAsync(directory, token);
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var row in expected)
        {
            await Assert.That(actual[row.Key]).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
        }
    }
}
