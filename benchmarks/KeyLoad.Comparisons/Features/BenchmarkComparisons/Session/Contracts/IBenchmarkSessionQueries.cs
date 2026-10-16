namespace KeyLoad.Comparisons;

/// <summary>Optional native lifecycle for indexes and read-cut queries over the same live document namespace.</summary>
public interface IBenchmarkSessionQueries
{
    /// <summary>Builds the declared native indexes without replacing or reseeding the namespace.</summary>
    Task BuildSessionIndexesAsync(CancellationToken cancellationToken);
    /// <summary>Executes and independently validates one deterministic bounded native query over stable records.</summary>
    Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken);
}
