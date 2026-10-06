using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoReplicaDeadline
{
    internal static CancellationTokenSource CreateOperation(IOptions<NativeComparisonExecutionOptions> options,
        CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(options).Value;
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            deadline.CancelAfter(execution.OperationTimeout);
            return deadline;
        }
        catch (Exception)
        {
            deadline.Dispose();
            throw;
        }
    }

    internal static CancellationTokenSource CreateCleanup(IOptions<NativeComparisonExecutionOptions> options)
        => new(NativeComparisonExecutionOptions.Require(options).Value.CleanupTimeout);
}
