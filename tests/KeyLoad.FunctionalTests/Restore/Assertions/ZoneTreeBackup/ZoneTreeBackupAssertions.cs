using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

/// <summary>Literal expectations for a ZoneTree backup, its restored store and the restored node's follow-up.</summary>
internal static class ZoneTreeBackupAssertions
{
    internal const long RestoreCommits = 1;
    private const string TreeDirectoryName = "tree";
    private const string PlainTreeDirectoryName = "plain-tree";

    internal static async Task LayoutAsync(ZoneTreeBackupFixture fixture, IReadOnlyDictionary<string, byte[]> backup,
        CancellationToken token)
    {
        await Assert.That(backup[ZoneTreePersistenceFormat.IdentityFileName]).IsEquivalentTo(
            await File.ReadAllBytesAsync(Path.Combine(fixture.Source, ZoneTreePersistenceFormat.IdentityFileName), token),
            CollectionOrdering.Matching);
        var tree = backup.Keys.Where(name => name != ZoneTreePersistenceFormat.IdentityFileName).ToArray();
        await Assert.That(tree).IsNotEmpty();
        await Assert.That(tree.All(name => name.StartsWith(TreeDirectoryName + Path.DirectorySeparatorChar,
            StringComparison.Ordinal) || name.StartsWith(PlainTreeDirectoryName + Path.DirectorySeparatorChar,
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(backup.Keys.Any(name => name.StartsWith(PlainTreeDirectoryName + Path.DirectorySeparatorChar,
            StringComparison.Ordinal))).IsTrue();
    }

    internal static async Task NewIdentityAsync(StoreIdentity source, StoreIdentity restored)
    {
        await Assert.That(restored.NodeId).IsNotEqualTo(source.NodeId);
        await Assert.That(restored.Incarnation).IsNotEqualTo(source.Incarnation);
        await Assert.That(restored.SigningKey.Span.SequenceEqual(source.SigningKey.Span)).IsFalse();
        await Assert.That(restored.DispatchPaused).IsTrue();
        await Assert.That(restored.Durability).IsEqualTo(source.Durability);
    }

    internal static async Task RestoredCutAsync(string directory, StoreIdentity restored, long position)
    {
        using var store = ZoneTreeBackupFixture.Open(directory);
        await StoreFileFixture.AssertPreservedIdentity(restored, store.Identity);
        await Assert.That(store.Position).IsGreaterThanOrEqualTo(position);
        await Assert.That(store.Position).IsEqualTo(position + RestoreCommits);
        await Assert.That(ZoneTreeBackupFixture.Read(store, ZoneTreeBackupFixture.BeforeNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 1);
        await Assert.That(ZoneTreeBackupFixture.Read(store, ZoneTreeBackupFixture.BeforeNamespace, 2))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 2);
        await Assert.That(ZoneTreeBackupFixture.Read(store, ZoneTreeBackupFixture.AfterNamespace, 1)).IsNull();
        var paused = store.Read(view => view.ReadOwnedValue(StoreFileFixture.DispatchPausedKey));
        await Assert.That(paused).IsNotNull();
        await Assert.That(OrleansSerialization.Deserialize<bool>(paused!)).IsTrue();
    }

    internal static async Task HealthyFollowUpAsync(string directory, StoreIdentity restored, long position)
    {
        using (var store = ZoneTreeBackupFixture.Open(directory))
        {
            await Assert.That(ZoneTreeBackupFixture.Commit(store, ZoneTreeBackupFixture.HealthyNamespace, 1))
                .IsEqualTo(position + RestoreCommits + 1);
            await store.SyncAsync();
        }
        using var cold = ZoneTreeBackupFixture.Open(directory);
        await StoreFileFixture.AssertPreservedIdentity(restored, cold.Identity);
        await Assert.That(cold.Position).IsEqualTo(position + RestoreCommits + 1);
        await Assert.That(ZoneTreeBackupFixture.Read(cold, ZoneTreeBackupFixture.HealthyNamespace, 1))
            .IsEqualTo(ZoneTreeBackupFixture.HealthyNamespace + 1);
        await Assert.That(ZoneTreeBackupFixture.Read(cold, ZoneTreeBackupFixture.BeforeNamespace, 2))
            .IsEqualTo(ZoneTreeBackupFixture.BeforeNamespace + 2);
    }
}
