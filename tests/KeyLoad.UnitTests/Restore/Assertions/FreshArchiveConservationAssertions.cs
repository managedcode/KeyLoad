using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;
using P = KeyLoad.UnitTests.Features.BackupRestore.FreshArchiveConservationProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class FreshArchiveConservationAssertions
{
    internal static (StoreIdentity Identity, long Position, string[] Rows) Capture(ZoneTreeStore store)
        => (store.Identity, store.Position, GraphShortestPathFullStoreImage.Bytes(store));

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task UnpublishedAsync(string root, string target, bool existingEmpty)
    {
        await Assert.That(Directory.Exists(target)).IsEqualTo(existingEmpty);
        if (existingEmpty)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse();
        }

        await Assert.That(Directory.EnumerateDirectories(root).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(existingEmpty
                ? new[] { P.Archive, P.Source, P.Source + TestDatabaseReplicaDirectory.Suffix, P.Target }.Order(StringComparer.Ordinal).ToArray()
                : new[] { P.Archive, P.Source, P.Source + TestDatabaseReplicaDirectory.Suffix }.Order(StringComparer.Ordinal).ToArray(),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(File.Exists(target)).IsFalse();
    }
}
