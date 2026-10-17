using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Search.NativeTextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderAuditImages
{
    private const int EmptyNativeInventory = 0;
    private const long EmptyNativeBytes = 0L;
    private const FileAttributes EmptyFileAttributes = 0;
    internal static (StoreIdentity Identity, long Position, string[] Rows) Capture(NativeTextProtectedRebuildColdOwner owner)
        => (NativeReadCutStoreStateAssertions.SnapshotIdentity(owner.Store), owner.Store.Position,
            QueueWholeFlowStorage.Bytes(owner.Store));

    internal static async Task SameAsync(NativeTextProtectedRebuildColdOwner owner,
        (StoreIdentity Identity, long Position, string[] Rows) expected)
    {
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(owner.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store)).IsEquivalentTo(expected.Rows, CollectionOrdering.Matching);
    }

    internal static string[] NativeFiles(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var entries = EmptyNativeInventory;
        var retainedBytes = EmptyNativeBytes;
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > NativeEntryBound)
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
                if (file.Length > NativeByteBound - retainedBytes)
                {
                    throw new InvalidOperationException(InventoryExceeded);
                }
                retainedBytes += file.Length;
                result.Add(Path.GetRelativePath(root, path) + RowSeparator + Convert.ToHexString(File.ReadAllBytes(path)));
            }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    internal static async Task ManifestAsync(NativeTextProtectedRebuildColdOwner owner, string path, byte[] original, CancellationToken token)
    {
        var manifest = NativeTextEnvelopeCodec.Decode<NativeTextManifest>(original);
        var identity = owner.Store.Identity;
        var scope = new TextProjectionScope(identity.NodeId, identity.Incarnation, identity.FormatVersion,
            identity.ReadGeneration, owner.Store.Position, owner.Source.Partition, Collection, Field,
            Root, InitialPolicyEpoch, InitialSchemaVersion);
        await Assert.That(manifest.FormatVersion).IsEqualTo(NativeTextProtocol.FormatVersion);
        await Assert.That(manifest.TokenizerVersion).IsEqualTo(TextProjectionProtocol.TokenizerVersion);
        await Assert.That(manifest.HashVersion).IsEqualTo(TextProjectionProtocol.HashVersion);
        await Assert.That(manifest.Scope).IsEqualTo(scope);
        NativeTextRecord[] records = [new(FirstNativeRecordId, new(owner.Source.Partition, Collection, Control), InitialRevision),
            new(SecondNativeRecordId, new(owner.Source.Partition, Collection, Ukrainian), ChangedRevision)];
        await Assert.That(JsonSerializer.Serialize(manifest.Records, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(records, JsonDefaults.Options));
        await Assert.That(manifest.Files.Length).IsGreaterThan(EmptyNativeInventory);
        foreach (var file in manifest.Files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(path)!, file.RelativePath), token);
            await Assert.That(bytes.LongLength).IsEqualTo(file.Length);
            await Assert.That(SHA256.HashData(bytes).AsSpan().SequenceEqual(file.Sha256)).IsTrue();
        }
    }

    internal static void RequireOriginalRoot(NativeTextProtectedRebuildColdOwner owner)
    {
        var root = owner.Source.Directory;
        if (!Path.IsPathFullyQualified(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(WrongRoot);
        }
    }
}
