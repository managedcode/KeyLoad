using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class SharedDomainRecoveryImage
{
    private const string RowSeparator = ":";

    internal static async Task IdentityAsync(IAtomicStore store, StoreIdentity digest)
    {
        var actual = store.Identity;
        await Assert.That(actual.FormatVersion).IsEqualTo(digest.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(digest.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(digest.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(digest.Incarnation);
        await Assert.That(actual.Durability).IsEqualTo(digest.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(digest.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(digest.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(digest.MinimumReaderContract);
        await Assert.That(SHA256.HashData(actual.SigningKey.Span).AsSpan().SequenceEqual(digest.SigningKey.Span)).IsTrue();
    }

    internal static async Task SeedOtherScopesAsync(IAtomicStore store, SharedDomainRecoveryInput input, bool committed)
    {
        var scope = SharedDomainCorpus.Create(Tenant, Database)[FirstIndex];
        var original = scope.Partition;
        var expectedApplied = SharedDomainRecoverySeed.AppliedPosition(input) + (committed ? FirstRevision : NoRevision);
        var appliedKey = Convert.ToHexString(KeySpace.AppliedBytes) + RowSeparator;
        var lastCommandKey = Convert.ToHexString(KeySpace.LastCommandBytes) + RowSeparator;
        var originalOutcome = KeySpace.PartitionOutcome(original, input.Original.UserId!, input.Original.CommandId);
        var expected = WithoutScope(input.SeedImage, original).Select(row =>
            row.StartsWith(appliedKey, StringComparison.Ordinal)
                ? appliedKey + Convert.ToHexString(OrleansSerialization.Serialize(expectedApplied))
                : committed && row.StartsWith(lastCommandKey, StringComparison.Ordinal)
                    ? lastCommandKey + Convert.ToHexString(OrleansSerialization.Serialize(originalOutcome)) : row);
        await Assert.That(store.Read(view => OrleansSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw new InvalidOperationException(RuntimeMissing)))).IsEqualTo(expectedApplied);
        await Assert.That(WithoutScope(SharedDomainStoredImage.Capture(store), original))
            .IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static IEnumerable<string> WithoutScope(IEnumerable<string> rows, PartitionRef partition)
        => rows.Where(row => !SharedDomainStoredImage.Owns(Convert.FromHexString(row[..row.IndexOf(RowSeparator, StringComparison.Ordinal)]), partition));

    internal static async Task SameAsync(IAtomicStore store, string[] rows, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(SharedDomainStoredImage.Capture(store))
            .IsEquivalentTo(rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static void RequireRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !System.IO.Directory.Exists(root) || !System.IO.Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(SharedDomainCrashProtocol.OriginalRootRequired);
        }
    }
}
