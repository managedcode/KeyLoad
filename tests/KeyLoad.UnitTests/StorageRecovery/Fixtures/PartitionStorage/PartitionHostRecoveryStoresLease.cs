using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionHostRecoveryStoresLease : IDisposable
{
    private readonly PartitionHostRecoveryAttempt attempt;

    internal PartitionHostRecoveryStoresLease(NodeOptions options, PartitionHostRecoveryAttempt attempt)
    {
        this.attempt = attempt;
        var runtime = ServerRuntimeTestOptions.Runtime(options);
        try
        {
            Stores = new(runtime.Node, options.DataDirectory, runtime.StorageExecution,
                runtime.PointCache, ownershipSettlement: attempt.Ownership);
        }
        catch (Exception failure)
        {
            attempt.RecordFailure(failure);
            throw;
        }
    }

    internal PartitionStores Stores
    {
        get;
    }

    public void Dispose()
    {
        try
        {
            Stores.Dispose();
        }
        catch (Exception failure)
        {
            attempt.RecordFailure(failure);
            throw;
        }
        attempt.RequireReleased();
    }
}
