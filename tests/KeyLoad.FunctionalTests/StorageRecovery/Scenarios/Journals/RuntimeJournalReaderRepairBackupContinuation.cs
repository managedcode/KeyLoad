using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class RuntimeJournalReaderRepairBackupContinuation
{
    private const int SourceRows = 2;
    private const int RestoredRows = 3;
    private static byte[] RestoredValue => [0x52, 0x4A, 0x33, 0xC9];

    internal static async Task RequireAsync(RuntimeJournalReaderFixture fixture, ZoneTreeStore source,
        StoreIdentity original, long position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireRowsAsync(source, restored: false, continued: false);
        await RequireIdentityAsync(original, source.Identity);
        await Assert.That(source.Position).IsEqualTo(position);
        await Assert.That(await source.CreateBackupAsync(fixture.BackupPath, token)).IsEqualTo(position);
        var backup = await RuntimeJournalReaderFixture.ReadInventoryAsync(fixture.BackupPath);
        var incarnation = Guid.NewGuid();
        var restored = await ZoneTreeStore.RestoreAsync(fixture.BackupPath, fixture.RestorePath,
            UnitExecutionOptions.StorageExecution(), incarnation, cancellationToken: token);
        await Assert.That(restored.NodeId).IsNotEqualTo(original.NodeId);
        await Assert.That(restored.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restored.SigningKey.Span.SequenceEqual(original.SigningKey.Span)).IsFalse();
        await Assert.That(restored.DispatchPaused).IsTrue();
        var restoredPosition = await ContinueRestoredAsync(fixture, restored, token);
        restoredPosition = await ColdAsync(fixture, restored, restoredPosition);
        await ColdAsync(fixture, restored, restoredPosition);
        await RequireBackupAsync(fixture, backup, token);
        await RequireIdentityAsync(original, source.Identity);
        await Assert.That(source.Position).IsEqualTo(position);
        await RequireRowsAsync(source, restored: false, continued: false);
    }

    private static async Task<long> ContinueRestoredAsync(RuntimeJournalReaderFixture fixture,
        StoreIdentity expected, CancellationToken token)
    {
        using var restored = fixture.Open(fixture.RestorePath, expected.Incarnation);
        await RequireIdentityAsync(expected, restored.Identity);
        var position = restored.Position;
        await Assert.That(position).IsGreaterThan(0L);
        await RequireRowsAsync(restored, restored: true, continued: false);
        token.ThrowIfCancellationRequested();
        RuntimeJournalReaderFixture.WriteRuntimeRecord(restored, RuntimeJournalReaderFixture.FollowupKey, RestoredValue);
        position = await NativeStoreAssertions.AdvancedAsync(restored, position);
        await RequireRowsAsync(restored, restored: true, continued: true);
        return position;
    }

    private static async Task<long> ColdAsync(RuntimeJournalReaderFixture fixture, StoreIdentity expected, long position)
    {
        using var cold = fixture.Open(fixture.RestorePath, expected.Incarnation);
        await RequireIdentityAsync(expected, cold.Identity);
        position = await NativeStoreAssertions.ReopenedAsync(cold, position);
        await RequireRowsAsync(cold, restored: true, continued: true);
        return position;
    }

    private static async Task RequireIdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await StoreFileFixture.AssertPreservedIdentity(expected, actual);
    }

    private static async Task RequireRowsAsync(ZoneTreeStore store, bool restored, bool continued)
    {
        var expected = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Convert.ToHexString(RuntimeJournalReaderFixture.RuntimeJournalKey)] = RuntimeJournalReaderFixture.RuntimeJournalValue,
            [Convert.ToHexString(RuntimeJournalReaderFixture.FollowupKey)] = continued ? RestoredValue : RuntimeJournalReaderFixture.FollowupValue
        };
        if (restored)
        {
            expected.Add(Convert.ToHexString(StoreFileFixture.DispatchPausedKey), OrleansSerialization.Serialize(true));
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

    private static async Task RequireBackupAsync(RuntimeJournalReaderFixture fixture,
        Dictionary<string, byte[]?> expected, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actual = await RuntimeJournalReaderFixture.ReadInventoryAsync(fixture.BackupPath);
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var row in expected)
        {
            if (row.Value is null)
            {
                await Assert.That(actual[row.Key]).IsNull();
            }
            else
            {
                await Assert.That(actual[row.Key]!).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
            }
        }
    }
}
