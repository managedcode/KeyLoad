using System.Collections.Immutable;
using KeyLoad.Artifacts;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Copies verified original archives off the joined source through the owning native artifact APIs.</summary>
internal static class ClusterRestoreRf3NativeArchive
{
    internal static async Task<ImmutableArray<string>> CopyAsync(TwoRf3MembershipWave source,
        ImmutableArray<ClusterBackupOwnerReceipt> receipts, string offNodeRoot, CancellationToken cancellationToken)
    {
        var joinedRoot = await source.StopForDirectoryReadAsync().ConfigureAwait(false);
        Directory.CreateDirectory(offNodeRoot);
        var paths = ImmutableArray.CreateBuilder<string>(receipts.Length);
        foreach (var receipt in receipts)
        {
            var node = receipt.Cut.Owner.PhysicalShardId == source.Profile.PhysicalShardId
                ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
            var original = Path.Combine(joinedRoot, node, ClusterRestoreRf3Protocol.ArchiveDirectory, receipt.ArchiveId);
            var artifact = Path.Combine(offNodeRoot, receipt.Cut.Owner.PhysicalShardId.ToString(
                ClusterRestoreRf3Protocol.ArchiveFormat) + ClusterRestoreRf3Protocol.ArtifactExtension);
            var unpacked = Path.Combine(offNodeRoot, receipt.Cut.Owner.PhysicalShardId.ToString(
                ClusterRestoreRf3Protocol.ArchiveFormat));
            BackupArtifact.PackCatalogBackup(original, artifact, ClusterRestoreRf3Protocol.ArtifactPieceBytes,
                receipt.ManifestDigest, IntegrationExecutionOptions.StorageExecution(), TimeProvider.System, cancellationToken);
            BackupArtifact.UnpackCatalogBackup(artifact, unpacked, receipt.ManifestDigest,
                IntegrationExecutionOptions.StorageExecution(), cancellationToken);
            var observed = ZoneTreeStore.ReadVerifiedCatalogBackup(unpacked,
                IntegrationExecutionOptions.StorageExecution(), cancellationToken);
            await Assert.That(observed.ManifestDigest).IsEqualTo(receipt.ManifestDigest);
            await Assert.That(observed.Position).IsEqualTo(receipt.Cut.StorePosition);
            await SqlRf3Protocol.EqualAsync(receipt.Cut,
                NativeSerialization.Deserialize<ClusterBackupOwnerCut>(observed.Metadata.Span));
            paths.Add(unpacked);
        }
        return paths.MoveToImmutable();
    }
    internal static async Task RequireOriginalAsync(ImmutableArray<ClusterBackupOwnerReceipt> receipts,
        ImmutableArray<string> archives, CancellationToken cancellationToken)
    {
        await Assert.That(archives.Length).IsEqualTo(receipts.Length);
        foreach (var (receipt, path) in receipts.Zip(archives))
        {
            var observed = ZoneTreeStore.ReadVerifiedCatalogBackup(path,
                IntegrationExecutionOptions.StorageExecution(), cancellationToken);
            await Assert.That(observed.ManifestDigest).IsEqualTo(receipt.ManifestDigest);
            await Assert.That(observed.Position).IsEqualTo(receipt.Cut.StorePosition);
            await SqlRf3Protocol.EqualAsync(receipt.Cut,
                NativeSerialization.Deserialize<ClusterBackupOwnerCut>(observed.Metadata.Span));
        }
    }
}
