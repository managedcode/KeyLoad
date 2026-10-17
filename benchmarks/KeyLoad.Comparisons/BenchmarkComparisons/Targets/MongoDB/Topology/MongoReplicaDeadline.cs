using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoReplicaDeadline
{
    internal static CancellationTokenSource CreateOperation(IOptions<NativeComparisonExecutionOptions> options, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(options).Value;
        var deadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
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

    internal static CancellationTokenSource CreateCleanup(IOptions<NativeComparisonExecutionOptions> options, TimeProvider timeProvider)
        => new(NativeComparisonExecutionOptions.Require(options).Value.CleanupTimeout, timeProvider);
}
