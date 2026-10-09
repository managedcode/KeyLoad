using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server;

internal static class ClusterBackupOwnerArchive
{
    private const string DirectoryName = "cluster-backups";
    private const string GuidFormat = "N";
    private const string InvalidArchive = "The retained cluster owner archive does not match its native capture identity.";

    internal static ClusterBackupOwnerReceipt Capture(PartitionHost partition, string principalId,
        ReadOnlyMemory<byte> ownedCapability, ReadExecutionBudget work)
    {
        var capability = NativeSerialization.Deserialize<ClusterBackupOwnerCapability>(ownedCapability.Span);
        var database = partition.Database;
        database.Store.Read(view =>
        {
            database.AuthorizeClusterBackupOwner(work.CreateView(view), principalId, capability, work);
            return true;
        });
        var request = capability.Request;
        var archiveId = request.CaptureId.ToString(GuidFormat);
        var directory = Path.Combine(partition.DirectoryPath, DirectoryName, archiveId);
        if (database.Store is not INativeCatalogBackupStore owner)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidArchive); }
        ClusterBackupOwnerCut? cut = null;
        var captured = owner.CaptureOrReadCatalogBackup(directory,
            view => database.AuthorizeClusterBackupOwner(work.CreateView(view), principalId, capability, work),
            (view, position, identity) => NativeSerialization.Serialize(database.CaptureClusterBackupOwner(view,
                principalId, request.CaptureId, position, identity, work)),
            original => cut = RequireCaptured(original, request), work.Cancellation);
        if (cut is null)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidArchive); }
        database.Store.Read(view =>
        {
            database.AuthorizeClusterBackupOwner(work.CreateView(view), principalId, capability, work);
            return true;
        });
        var result = new ClusterBackupOwnerReceipt(ClusterBackupOwnerReceipt.CurrentVersion,
            archiveId, cut, captured.ManifestDigest);
        work.CheckResult(result);
        work.Check();
        return result;
    }

    private static ClusterBackupOwnerCut RequireCaptured(NativeCatalogBackupCapture captured,
        ClusterBackupOwnerRequest request)
    {
        var cut = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(captured.Metadata.Span);
        if (cut is null || cut.Version != ClusterBackupOwnerCut.CurrentVersion
            || cut.CaptureId != request.CaptureId || cut.SourceNodeId != request.ExpectedNodeId
            || cut.StorePosition != captured.Position
            || !PhysicalOwnerEntryValidation.SameOwner(cut.Owner, request.ExpectedOwner))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidArchive); }
        return cut;
    }
}
