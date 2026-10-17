namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget : IBenchmarkSessionQueries
{
    /// <inheritdoc/>
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
        => KeyLoadSessionQueryExecution.BuildIndexesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
        => KeyLoadSessionQueryExecution.QueryAsync(client, partition, ordinal, stableStart, stableCount, cancellationToken);
}
