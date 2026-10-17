using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RuntimeJournalReaderRepairBackupContinuation
{
    private const int SourceRows = 2;
    private const int RestoredRows = 3;
    private const long RestoreIncrement = 1;
    private const long HealthyIncrement = 1;
    private static byte[] RestoredValue => [0x52, 0x4A, 0x33, 0xC9];

    internal static async Task RequireAsync(RuntimeJournalReaderFixture fixture, ZoneTreeStore source,
        StoreIdentity original, long position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireRowsAsync(source, restored: false, continued: false);
        source.SetDispatchPaused(true);
        var paused = original with
        {
            DispatchPaused = true
        };
        await RequireIdentityAsync(paused, source.Identity);
        await Assert.That(source.Position).IsEqualTo(position);
        await Assert.That(source.CreateBackup(fixture.BackupPath)).IsEqualTo(position);
        var backup = await RuntimeJournalReaderFixture.ReadInventoryAsync(fixture.BackupPath);
        var verified = ZoneTreeStore.VerifyBackup(fixture.BackupPath, UnitExecutionOptions.StorageExecution());
        await Assert.That(verified.Position).IsEqualTo(position);
        await RequireIdentityAsync(paused, verified.Identity);
        var incarnation = Guid.NewGuid();
        var restored = ZoneTreeStore.Restore(fixture.BackupPath, fixture.RestorePath,
            UnitExecutionOptions.StorageExecution(), incarnation);
        await Assert.That(restored.NodeId).IsNotEqualTo(original.NodeId);
        await Assert.That(restored.Incarnation).IsEqualTo(incarnation);
        await Assert.That(restored.SigningKey.Span.SequenceEqual(original.SigningKey.Span)).IsFalse();
        await Assert.That(restored.DispatchPaused).IsTrue();
        await Assert.That(restored.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
        await ContinueRestoredAsync(fixture, restored, position, token);
        await ColdAsync(fixture, restored, position + RestoreIncrement + HealthyIncrement);
        await ColdAsync(fixture, restored, position + RestoreIncrement + HealthyIncrement);
        await RequireBackupAsync(fixture, backup, token);
        await RequireIdentityAsync(paused, source.Identity);
        await Assert.That(source.Position).IsEqualTo(position);
        await RequireRowsAsync(source, restored: false, continued: false);
    }

    private static async Task ContinueRestoredAsync(RuntimeJournalReaderFixture fixture,
        StoreIdentity expected, long position, CancellationToken token)
    {
        using var restored = fixture.Open(fixture.RestorePath, expected.Incarnation);
        await RequireIdentityAsync(expected, restored.Identity);
        await Assert.That(restored.Position).IsEqualTo(position + RestoreIncrement);
        await RequireRowsAsync(restored, restored: true, continued: false);
        token.ThrowIfCancellationRequested();
        RuntimeJournalReaderFixture.WriteRuntimeRecord(restored, RuntimeJournalReaderFixture.FollowupKey, RestoredValue);
        await Assert.That(restored.Position).IsEqualTo(position + RestoreIncrement + HealthyIncrement);
        await RequireRowsAsync(restored, restored: true, continued: true);
    }

    private static async Task ColdAsync(RuntimeJournalReaderFixture fixture, StoreIdentity expected, long position)
    {
        using var cold = fixture.Open(fixture.RestorePath, expected.Incarnation);
        await RequireIdentityAsync(expected, cold.Identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await RequireRowsAsync(cold, restored: true, continued: true);
    }

    private static async Task RequireIdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await WalFileFixture.AssertPreservedIdentity(expected, actual);
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
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
