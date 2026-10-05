using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>One of the two immutable, bounded S1 scaled comparison profiles.</summary>
[JsonConverter(typeof(ScaledComparisonProfileJsonConverter))]
public sealed record ScaledComparisonProfile : IComparisonSettings
{
    internal ScaledComparisonProfile(string id, int documents)
    {
        Id = id;
        Documents = documents;
    }

    /// <summary>Gets the exact accepted profile identifier.</summary>
    public string Id { get; }
    /// <inheritdoc />
    public int Documents { get; }
    /// <inheritdoc />
    public int Operations => ScaledComparisonProfileValues.Scale100kCount;
    /// <inheritdoc />
    public int Warmup => ScaledComparisonProfileValues.WarmupQueries;
    /// <inheritdoc />
    public int Repetitions => ScaledComparisonProfileValues.RepetitionCount;
    /// <inheritdoc />
    public int Concurrency => ScaledComparisonProfileValues.QueryConcurrency;
    /// <inheritdoc />
    public int PayloadBytes => ScaledComparisonProfileValues.PayloadBytes;
    /// <inheritdoc />
    public int Seed => ScaledComparisonProfileValues.CorpusSeed;
    /// <inheritdoc />
    public int Dimensions => ScaledComparisonProfileValues.VectorDimensions;
    /// <inheritdoc />
    public int TopK => ScaledComparisonProfileValues.TopKNeighbors;
    /// <inheritdoc />
    public int TimeoutSeconds => ScaledComparisonProfileValues.OperationTimeoutSeconds;
    /// <inheritdoc />
    public int GraphVertices => ScaledComparisonProfileValues.NoGraphEntities;
    /// <inheritdoc />
    public int GraphFanOut => ScaledComparisonProfileValues.NoGraphEntities;
    /// <inheritdoc />
    public int GraphDepth => ScaledComparisonProfileValues.NoGraphEntities;
}
