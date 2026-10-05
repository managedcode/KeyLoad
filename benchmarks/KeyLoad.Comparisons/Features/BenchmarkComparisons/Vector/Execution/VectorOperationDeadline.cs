namespace KeyLoad.Comparisons;

internal static class VectorOperationDeadline
{
    internal static CancellationTokenSource Create(NativeComparisonExecutionOptions execution, CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(execution.OperationTimeout);
        return deadline;
    }
}
