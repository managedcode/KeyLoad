using System.Collections.Immutable;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ArchiveMutationTrial
{
    internal const string ModifiedDetail = "A backup file failed verification.";
    private const string DerivativeDirectory = "modified-native-journal";
    private const string JournalFile = "commands.wal";
    private const int FirstOwner = 0;

    internal static async Task<ImmutableArray<string>> RequireAndRepairAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ImmutableArray<string> archives,
        string ownedRoot, CancellationToken cancellationToken)
    {
        var copies = await ClusterRestoreRf3ArchiveOmissionTrial.CopyOriginalsAsync(originals, archives,
            Path.Combine(ownedRoot, DerivativeDirectory), cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(copies[FirstOwner], JournalFile);
        var original = await ClusterRestoreRf3ArchiveJournalMutation.ChangeAsync(path, cancellationToken).ConfigureAwait(false);
        var failures = new List<Exception>();
        var joined = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Directory.Exists(ClusterRestoreRf3RetainedCut.OperationRoot(target))).IsFalse();
            await target.StartDerivativeArchiveAsync(copies, rejectModified: true, cancellationToken).ConfigureAwait(false);
            await Assert.That(Directory.Exists(ClusterRestoreRf3RetainedCut.OperationRoot(target))).IsFalse();
            await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, archives, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await target.StopAsync().ConfigureAwait(false);
            joined = true;
        }, failures).ConfigureAwait(false);
        if (joined)
        {
            await ServerFailureObserver.ObserveAsync(() => ClusterRestoreRf3ArchiveJournalMutation.RepairAsync(
                path, original, cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await ClusterRestoreRf3NativeArchive.RequireOriginalAsync(originals, copies, cancellationToken).ConfigureAwait(false);
        return copies;
    }
}
