using KeyLoad.RecoveryTests.StorageRecovery;
using KeyLoad.Server;
using P = KeyLoad.RecoveryTests.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.RecoveryTests.AnalyticsStore;

internal static class UserAnalyticsDerivedProcessFiles
{
    internal static async Task RequireReleasedAsync(string root, CancellationToken token)
    {
        var physical = Path.Combine(root, P.PhysicalDirectory);
        await KilledProcessFileReadiness.WaitAsync(Path.Combine(physical, PartitionStoreProtocol.CanonicalDirectory), token);
        token.ThrowIfCancellationRequested();
        using var ownership = new FileStream(Path.Combine(physical, PartitionStoreProtocol.OwnershipFile),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        RecoveryFileInventory.AssertHandlesReleased(physical);
    }

    internal static void RequireDerived(string root, bool abandoned)
    {
        var directory = Path.Combine(root, P.PhysicalDirectory, UserAnalyticsGenerationPaths.DirectoryName);
        var populated = Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
        if (populated != abandoned)
        {
            throw new InvalidOperationException(P.Invalid);
        }
    }
}
