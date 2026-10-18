using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeBackupJournalRepairAssertions
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
            var opened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
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
        await Assert.That(NativeSerialization.Serialize(current.Identity)
            .SequenceEqual(NativeSerialization.Serialize(original.Identity))).IsTrue();
        await Assert.That(current.Rows).IsEquivalentTo(original.Rows, CollectionOrdering.Matching);
    }

    internal static async Task RestoredAsync(NativeBackupCutFixture fixture, StoreIdentity identity,
        (StoreIdentity Identity, long Position, string[] Rows) actual)
    {
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Identity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(fixture.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.Durability).IsEqualTo(fixture.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(fixture.Position + 1L);
        await Assert.That(NativeSerialization.Serialize(actual.Identity)
            .SequenceEqual(NativeSerialization.Serialize(identity))).IsTrue();
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" +
                Convert.ToHexString(NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" +
                Convert.ToHexString(NativeSerialization.Serialize(true)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.LastAppliedKey)) + ":" +
                Convert.ToHexString(NativeSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.ClockKey)) + ":" +
                Convert.ToHexString(NativeSerialization.Serialize(1L))
        ];
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
    }

    internal static async Task OriginalSourceAsync(NativeBackupCutFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source)
    {
        const long CapturedPosition = 2L;
        const long InitialAuthorityValue = 1L;
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" + Convert.ToHexString(NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(InitialAuthorityValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.ClockKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(InitialAuthorityValue))
        ];
        await Assert.That(source.Position).IsEqualTo(CapturedPosition);
        await Assert.That(source.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(source.Identity)
            .SequenceEqual(NativeSerialization.Serialize(fixture.Identity))).IsTrue();
    }

    internal static async Task OriginalSourceAndArchiveAsync(NativeBackupCutFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, Dictionary<string, byte[]> archive,
        CancellationToken token)
    {
        await SameCutAsync(source, Read(fixture.Source, token));
        await SameArchiveAsync(archive, fixture.Backup, token);
    }

    internal static async Task RepairArchiveAsync(string backup, Dictionary<string, byte[]> original)
    {
        foreach (var row in original)
        {
            await File.WriteAllBytesAsync(Path.Combine(backup, row.Key), row.Value, CancellationToken.None);
        }
        await SameArchiveAsync(original, backup, CancellationToken.None);
    }

}
