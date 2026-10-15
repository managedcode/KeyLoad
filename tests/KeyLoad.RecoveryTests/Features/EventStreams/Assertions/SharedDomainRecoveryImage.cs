using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

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

    internal static async Task SeedOtherScopesAsync(IAtomicStore store, SharedDomainRecoveryInput input)
    {
        var original = SharedDomainCorpus.Create(Tenant, Database)[FirstIndex].Partition;
        var expected = WithoutScope(input.SeedImage, original);
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
