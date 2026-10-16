namespace KeyLoad.Comparisons.Targets;

public sealed partial class Neo4jTarget : IBenchmarkSessionQueries
{
    /// <inheritdoc/>
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
        => Neo4jSessionQueryExecution.BuildIndexesAsync(label, execution, QueryAsync, cancellationToken);

    /// <inheritdoc/>
    public Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
        => Neo4jSessionQueryExecution.QueryAsync(label, QueryAsync, ordinal, stableStart, stableCount, cancellationToken);
}
