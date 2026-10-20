using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Search.TextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextProviderAuditImages
{
    private const int RuntimeEmptyInventory = 0;
    private const long RuntimeEmptyBytes = 0L;
    private const FileAttributes EmptyFileAttributes = 0;
    internal static (StoreIdentity Identity, long Position, string[] Rows) Capture(TextProtectedRebuildColdOwner owner)
        => (ReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store));

    internal static async Task SameAsync(TextProtectedRebuildColdOwner owner,
        (StoreIdentity Identity, long Position, string[] Rows) expected)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store)).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
    }

    internal static string[] RuntimeFiles(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var entries = RuntimeEmptyInventory;
        var retainedBytes = RuntimeEmptyBytes;
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > EntryBound)
                {
                    throw new InvalidOperationException(InventoryExceeded);
                }
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != EmptyFileAttributes)
                {
                    throw new InvalidOperationException(InventoryExceeded);
                }
                if ((attributes & FileAttributes.Directory) != EmptyFileAttributes)
                {
                    result.Add(Path.GetRelativePath(root, path) + DirectoryMarker);
                    pending.Push(path);
                    continue;
                }
                var file = new FileInfo(path);
                if (file.Length > RuntimeByteBound - retainedBytes)
                {
                    throw new InvalidOperationException(InventoryExceeded);
                }
                retainedBytes += file.Length;
                result.Add(Path.GetRelativePath(root, path) + RowSeparator + Convert.ToHexString(File.ReadAllBytes(path)));
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    internal static async Task ManifestAsync(TextProtectedRebuildColdOwner owner, string path, byte[] original, CancellationToken token)
    {
        var manifest = TextEnvelopeCodec.Decode<TextManifest>(original);
        var identity = owner.Store.Identity;
        var scope = new TextProjectionScope(identity.NodeId, identity.Incarnation, identity.FormatVersion,
            identity.ReadGeneration, owner.Store.Position, owner.Source.Partition, Collection, Field,
            Root, InitialPolicyEpoch, InitialSchemaVersion);
        await Assert.That(manifest.FormatVersion).IsEqualTo(TextProtocol.FormatVersion);
        await Assert.That(manifest.TokenizerVersion).IsEqualTo(TextProjectionProtocol.TokenizerVersion);
        await Assert.That(manifest.HashVersion).IsEqualTo(TextProjectionProtocol.HashVersion);
        await Assert.That(manifest.Scope).IsEqualTo(scope);
        TextRecord[] records = [new(RuntimeFirstRecordId, owner.Reference(new(owner.Source.Partition, Collection, Control)), InitialRevision),
            new(SecondRecordId, owner.Reference(new(owner.Source.Partition, Collection, Ukrainian)), ChangedRevision)];
        await Assert.That(JsonSerializer.Serialize(manifest.Records, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(records, JsonDefaults.Options));
        await Assert.That(manifest.Files.Length).IsGreaterThan(RuntimeEmptyInventory);
        foreach (var file in manifest.Files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(path)!, file.RelativePath), token);
            await Assert.That(bytes.LongLength).IsEqualTo(file.Length);
            await Assert.That(SHA256.HashData(bytes).AsSpan().SequenceEqual(file.Sha256)).IsTrue();
        }
    }

    internal static void RequireOriginalRoot(TextProtectedRebuildColdOwner owner)
    {
        var root = owner.Source.Directory;
        if (!Path.IsPathFullyQualified(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(WrongRoot);
        }
    }
}
