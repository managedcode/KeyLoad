namespace KeyLoad.Comparisons;

/// <summary>Provides optional, failure-only diagnostics owned by one comparison target.</summary>
internal interface IComparisonFailureDiagnostics
{
    /// <summary>Observes a settled failed case without changing its outcome or measurement.</summary>
    /// <param name="failed">The original failed case.</param>
    /// <param name="cancellationToken">The parent run cancellation token.</param>
    /// <returns>A task that completes after the bounded observation and output attempt settle.</returns>
    Task ObserveFailureAsync(ComparisonCase failed, CancellationToken cancellationToken);
}
