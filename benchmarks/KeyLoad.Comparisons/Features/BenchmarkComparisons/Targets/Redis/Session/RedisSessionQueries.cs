namespace KeyLoad.Comparisons.Targets;

public sealed partial class RedisTarget : IBenchmarkSessionQueries
{
    /// <inheritdoc/>
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
        => RedisSessionQueryExecution.BuildIndexesAsync(cancellationToken);

    /// <inheritdoc/>
    public Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
        => RedisSessionQueryExecution.QueryAsync(connection, prefix, ordinal, stableStart, stableCount, cancellationToken);
}
