namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Complete stopped operation and actual published target images for a fixture-owned fault.</summary>
internal static class ClusterRestoreRf3FaultImage
{
    internal static (byte[] Operation, byte[]? Target) Capture(ClusterRestoreRf3Fixture target)
    {
        if (File.Exists(target.DataRoot))
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        var operation = ClusterRestoreRf3RetainedCut.Operation(target);
        var published = Directory.Exists(target.DataRoot)
            ? ClusterRestoreRf3RetainedCut.Target(target) : null;
        return (operation, published);
    }
}
