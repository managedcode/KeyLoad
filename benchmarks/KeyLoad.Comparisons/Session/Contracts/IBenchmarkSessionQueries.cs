namespace KeyLoad.Comparisons;

/// <summary>Optional execution lifecycle for indexes and read-cut queries over the same live document namespace.</summary>
public interface IBenchmarkSessionQueries
{
    /// <summary>Builds the declared execution indexes without replacing or reseeding the namespace.</summary>
    Task BuildSessionIndexesAsync(CancellationToken cancellationToken);
    /// <summary>Executes and independently validates one deterministic bounded execution query over stable records.</summary>
    Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken);
}
