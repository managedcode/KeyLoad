using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using P = KeyLoad.CrashHost.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

internal static class UserAnalyticsDerivedProcessFiles
{
    internal static async Task RequireReleasedAsync(string root, CancellationToken token)
    {
        var physical = Path.Combine(root, P.PhysicalDirectory);
        await KilledProcessFileReadiness.WaitAsync(Path.Combine(physical, PartitionStoreProtocol.CanonicalDirectory), token);
        await KilledProcessFileReadiness.WaitAsync(Path.Combine(physical, ReplicaProtocol.ReplicaDirectory), token);
        token.ThrowIfCancellationRequested();
        using var ownership = new FileStream(Path.Combine(physical, PartitionStoreProtocol.OwnershipFile),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        RecoveryFileInventory.AssertNativeHandlesReleased(physical);
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
