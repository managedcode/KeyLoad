namespace KeyLoad.Comparisons.Targets;

public sealed partial class MongoTarget : IBenchmarkSessionQueries
{
    /// <inheritdoc/>
    public Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
        => MongoSessionQueryExecution.BuildIndexesAsync(documents, cancellationToken);

    /// <inheritdoc/>
    public Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
        => MongoSessionQueryExecution.QueryAsync(documents, ordinal, stableStart, stableCount, cancellationToken);
}
