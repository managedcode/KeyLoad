namespace KeyLoad.Comparisons;

internal static class VectorOperationDeadline
{
    internal static CancellationTokenSource Create(NativeComparisonExecutionOptions execution, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var deadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
        deadline.CancelAfter(execution.OperationTimeout);
        return deadline;
    }
}
