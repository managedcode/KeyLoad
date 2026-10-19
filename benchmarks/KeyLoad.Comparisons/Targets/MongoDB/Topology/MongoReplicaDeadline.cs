using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoReplicaDeadline
{
    internal static CancellationTokenSource CreateOperation(IOptions<ComparisonExecutionOptions> options, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var execution = ComparisonExecutionOptions.Require(options).Value;
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

    internal static CancellationTokenSource CreateCleanup(IOptions<ComparisonExecutionOptions> options, TimeProvider timeProvider)
        => new(ComparisonExecutionOptions.Require(options).Value.CleanupTimeout, timeProvider);
}
