using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingCatalogRestore
{
    private const long NoAppliedIndex = 0;
    private const int CatalogReconciliationCommitCount = 1;
    private const int AuthorityResetCommitCount = 1;
    private const string FirstEndpoint = "http://127.0.0.1:18001/";
    private const string SecondEndpoint = "http://127.0.0.1:18002/";
    private const string ThirdEndpoint = "http://127.0.0.1:18003/";

    internal static NativeCatalogBackupCapture Capture(TestDatabase source, string backup, CancellationToken token)
    {
        var captureId = Guid.NewGuid();
        var work = source.CreateReadWork(token);
        return source.Store.CreateCatalogBackup(backup, (view, position, identity) =>
            NativeSerialization.Serialize(source.Database.CaptureClusterBackupOwner(view,
                EventingArtifactFixture.Principal, captureId, position, identity, work)), token);
    }

    internal static async Task<long> RestoreAsync(TestDatabase source, NativeCatalogBackupCapture capture,
        string unpacked, string destination, CancellationToken token)
    {
        var original = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(capture.Metadata.Span);
        await Assert.That(original.StorePosition).IsEqualTo(capture.Position);
        await Assert.That(original.SourceNodeId).IsEqualTo(source.Store.Identity.NodeId);
        await Assert.That(original.AppliedIndex).IsEqualTo(source.Database.LastApplied);
        var targetOwner = original.Owner with { PhysicalShardId = Guid.NewGuid(), Incarnation = Guid.NewGuid() };
        var mapping = new ClusterRestoreOwnerMapping(ClusterRestoreOwnerMapping.CurrentVersion,
            original.Owner, targetOwner, [FirstEndpoint, SecondEndpoint, ThirdEndpoint]);
        var signingKey = RandomNumberGenerator.GetBytes(source.Store.Identity.SigningKey.Length);
        StoreIdentity identity;
        try
        {
            var work = source.CreateReadWork(token);
            identity = ZoneTreeStore.RestoreCatalogBackup(unpacked, destination, source.StorageExecution,
                (transaction, sourceIdentity, position, metadata) => source.ReconcileCatalogRestore(transaction,
                    sourceIdentity, position, metadata, [original], [mapping], work),
                targetOwner.Incarnation, signingKey, token);
        }
        finally { CryptographicOperations.ZeroMemory(signingKey); }
        await Assert.That(identity.Incarnation).IsEqualTo(targetOwner.Incarnation);
        await Assert.That(identity.NodeId).IsNotEqualTo(original.SourceNodeId);
        await Assert.That(identity.DispatchPaused).IsTrue();
        var carriedOrigins = original.Partitions.Count(partition => partition.Roster.FirstSeenAppliedIndex > NoAppliedIndex);
        return checked(capture.Position + carriedOrigins + CatalogReconciliationCommitCount + AuthorityResetCommitCount);
    }
}
