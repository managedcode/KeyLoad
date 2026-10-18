using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeIdentityDomainRepairCold
{
    private const string SourceHealthy = "identity-authority-source-healthy";
    private const string RestoredHealthy = "identity-authority-restored-healthy";
    private const string Value = "literal-current-authority-continuation";
    private const string ContinuedArchive = "continued-backup";
    private const int ColdOwners = 2;

    internal static async Task RunAsync(string root, string source, string originalBackup, string target,
        (StoreIdentity Identity, long Position, string[] Rows) original, CancellationToken token)
    {
        var backup = Path.Combine(root, ContinuedArchive);
        await NativeIdentityDomainRepairFlow.OwnAsync(source, async store =>
        {
            Write(store, SourceHealthy);
            await Assert.That(store.Position).IsEqualTo(original.Position + 1L);
            await SameIdentityAsync(original.Identity, store.Identity);
            await RowsAsync(store, restored: false, continued: false);
            store.CreateBackup(backup);
        }, token);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(backup, token);
        var verified = ZoneTreeStore.VerifyBackup(backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(original.Position + 1L);
        await SameIdentityAsync(original.Identity, verified.Identity);
        var restored = ZoneTreeStore.Restore(backup, target, UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        await Assert.That(restored.NodeId != original.Identity.NodeId).IsTrue();
        await Assert.That(restored.Incarnation != original.Identity.Incarnation).IsTrue();
        await Assert.That(restored.SigningKey.Span.SequenceEqual(original.Identity.SigningKey.Span)).IsFalse();
        await SameIdentityAsync(original.Identity, restored with
        {
            NodeId = original.Identity.NodeId,
            Incarnation = original.Identity.Incarnation,
            SigningKey = original.Identity.SigningKey,
            DispatchPaused = original.Identity.DispatchPaused
        });
        await Assert.That(restored.DispatchPaused).IsTrue();
        await NativeIdentityDomainRepairFlow.OwnAsync(target, async store =>
        {
            await Assert.That(store.Position).IsEqualTo(original.Position + 2L);
            await RowsAsync(store, restored: true, continued: false);
            Write(store, RestoredHealthy);
            await Assert.That(store.Position).IsEqualTo(original.Position + 3L);
            await RowsAsync(store, restored: true, continued: true);
        }, token);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await NativeIdentityDomainRepairFlow.OwnAsync(target, async store =>
            {
                await Assert.That(store.Position).IsEqualTo(original.Position + 3L);
                await SameIdentityAsync(restored, store.Identity);
                await RowsAsync(store, restored: true, continued: true);
            }, token);
            await NativeIdentityDomainRepairFlow.OwnAsync(source, async store =>
            {
                await Assert.That(store.Position).IsEqualTo(original.Position + 1L);
                await SameIdentityAsync(original.Identity, store.Identity);
                await RowsAsync(store, restored: false, continued: false);
            }, token);
        }
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token);
        await Assert.That(Directory.Exists(originalBackup)).IsTrue();
    }

    private static void Write(ZoneTreeStore store, string key)
        => store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(key), Value);
            return true;
        });

    private static async Task RowsAsync(ZoneTreeStore store, bool restored, bool continued)
    {
        var expected = new List<string>
        {
            Row(MetadataBackupFixture.StoredKeyBytes, NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Row(KeyCodec.Encode(SourceHealthy), NativeSerialization.Serialize(Value))
        };
        if (restored)
        {
            expected.Add(Row(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey), NativeSerialization.Serialize(true)));
        }
        if (continued)
        {
            expected.Add(Row(KeyCodec.Encode(RestoredHealthy), NativeSerialization.Serialize(Value)));
        }
        await Assert.That(EventingArtifactState.FullBytes(store))
            .IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    private static string Row(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        => Convert.ToHexString(key) + ":" + Convert.ToHexString(value);

    private static async Task SameIdentityAsync(StoreIdentity expected, StoreIdentity actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
