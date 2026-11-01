using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupCutAssertions
{
    private const int MaximumFixtureRows = 128;
    private const int MaximumArchiveFiles = 16;
    private const long MaximumArchiveBytes = 1_048_576;

    internal static (StoreIdentity Identity, long Position, string[] Rows) Read(string directory,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ZoneTreeStore? store = null;
        (StoreIdentity Identity, long Position, string[] Rows) result = default;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            var opened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution());
            store = opened;
            result = opened.Read(view =>
            {
                token.ThrowIfCancellationRequested();
                var page = view.Scan([], MaximumFixtureRows);
                if (page.HasMore)
                {
                    throw new InvalidOperationException("The original backup fixture exceeded its complete-row bound.");
                }
                return (opened.Identity, opened.Position, page.Records.Select(row =>
                    Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
            });
        }, failures);
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    internal static async Task<Dictionary<string, byte[]>> ArchiveAsync(string directory,
        CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            bytes = checked(bytes + new FileInfo(file).Length);
            if (result.Count >= MaximumArchiveFiles || bytes > MaximumArchiveBytes)
            {
                throw new InvalidOperationException("The original backup archive exceeded its fixture bound.");
            }
            result.Add(Path.GetRelativePath(directory, file), await File.ReadAllBytesAsync(file, token));
        }
        return result;
    }

    internal static async Task SameArchiveAsync(Dictionary<string, byte[]> original, string directory,
        CancellationToken token)
    {
        var current = await ArchiveAsync(directory, token);
        await Assert.That(current.Keys).IsEquivalentTo(original.Keys);
        foreach (var row in original)
        {
            await Assert.That(current[row.Key]).IsEquivalentTo(row.Value, CollectionOrdering.Matching);
        }
    }

    internal static async Task SameCutAsync((StoreIdentity Identity, long Position, string[] Rows) original,
        (StoreIdentity Identity, long Position, string[] Rows) current)
    {
        await Assert.That(current.Position).IsEqualTo(original.Position);
        await Assert.That(OrleansSerialization.Serialize(current.Identity)
            .SequenceEqual(OrleansSerialization.Serialize(original.Identity))).IsTrue();
        await Assert.That(current.Rows).IsEquivalentTo(original.Rows, CollectionOrdering.Matching);
    }

    internal static async Task RestoredAsync(BackupCutFixture fixture, StoreIdentity identity,
        (StoreIdentity Identity, long Position, string[] Rows) actual)
    {
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Identity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(fixture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.Durability).IsEqualTo(fixture.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(fixture.Position + 1L);
        await Assert.That(OrleansSerialization.Serialize(actual.Identity)
            .SequenceEqual(OrleansSerialization.Serialize(identity))).IsTrue();
        string[] expected =
        [
            Row(BackupCutFixture.StoredKeyBytes, OrleansSerialization.Serialize(BackupCutFixture.StoredValue)),
            Row(StoreFileFixture.DispatchPausedKey, OrleansSerialization.Serialize(true)),
            Row(KeySpace.Applied.ToArray(), OrleansSerialization.Serialize(BackupCutFixture.InitialAuthorityValue)),
            Row(KeySpace.Clock.ToArray(), OrleansSerialization.Serialize(BackupCutFixture.InitialAuthorityValue)),
            Row(StoreFileFixture.PositionKey, StoreFileFixture.PositionValue(fixture.Position + 1L))
        ];
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
    }

    /// <summary>Reads the store identity a backup copied from its source.</summary>
    internal static StoreIdentity BackupIdentity(string backup)
    {
        var settings = UnitExecutionOptions.StorageExecution().Value;
        return ZoneTreeIdentityFile.Read(Path.Combine(backup, ZoneTreePersistenceFormat.IdentityFileName),
            settings.MaximumIdentityFileBytes, settings.StreamBufferBytes);
    }

    private static string Row(byte[] key, byte[] value) => Convert.ToHexString(key) + ":" + Convert.ToHexString(value);
}
