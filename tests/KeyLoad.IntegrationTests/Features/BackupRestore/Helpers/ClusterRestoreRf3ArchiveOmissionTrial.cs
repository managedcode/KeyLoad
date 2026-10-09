using System.Collections.Immutable;
using KeyLoad.Artifacts;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Only a verified derivative archive is omitted; every original off-node archive remains immutable.</summary>
internal static class ClusterRestoreRf3ArchiveOmissionTrial
{
    internal const string MissingDetail = "The native catalog archive metadata is invalid.";
    private const string DerivativeDirectory = "missing-catalog-envelope";
    private const string CatalogEnvelopeFile = "catalog-backup.native";
    private const int FirstOwner = 0;

    internal static async Task RequireAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ImmutableArray<string> archives,
        string ownedRoot, CancellationToken cancellationToken)
    {
        var root = Path.Combine(ownedRoot, DerivativeDirectory);
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        var copies = ImmutableArray.CreateBuilder<string>(originals.Length);
        foreach (var (original, archive) in originals.Zip(archives))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copy = Path.Combine(root, original.Cut.Owner.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.ArchiveFormat));
            BackupArtifact.UnpackCatalogBackup(archive + ClusterRestoreRf3Protocol.ArtifactExtension, copy,
                original.ManifestDigest, IntegrationExecutionOptions.StorageExecution(), cancellationToken);
            copies.Add(copy);
        }
        File.Delete(Path.Combine(copies[FirstOwner], CatalogEnvelopeFile));
        await target.StartRejectedMissingAsync(copies.MoveToImmutable(), cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
    }
}
