namespace KeyLoad.Comparisons;

internal static class ComparisonFailureDiagnostics
{
    internal static Task ObserveAsync(IComparisonTarget target, ComparisonCase failed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(failed);
        return target is IComparisonFailureDiagnostics diagnostics
            ? diagnostics.ObserveFailureAsync(failed, cancellationToken)
            : Task.CompletedTask;
    }
}
