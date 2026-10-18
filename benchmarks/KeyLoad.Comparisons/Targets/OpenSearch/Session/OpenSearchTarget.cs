namespace KeyLoad.Comparisons.Targets;

public sealed partial class OpenSearchTarget : IBenchmarkSessionQueries
{
    /// <inheritdoc/>
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
        => OpenSearchSessionQueryExecution.BuildIndexesAsync(client, index, timeProvider, executionOptions, cancellationToken);

    /// <inheritdoc/>
    public Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
        => OpenSearchSessionQueryExecution.QueryAsync(client, index, ordinal, stableStart, stableCount, cancellationToken);
}
