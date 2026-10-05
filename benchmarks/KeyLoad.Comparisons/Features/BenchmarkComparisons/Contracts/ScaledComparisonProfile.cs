using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>One of the three immutable, bounded S1 scaled comparison profiles.</summary>
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
    public int Operations => 100_000;
    /// <inheritdoc />
    public int Warmup => 256;
    /// <inheritdoc />
    public int Repetitions => 1;
    /// <inheritdoc />
    public int Concurrency => 16;
    /// <inheritdoc />
    public int PayloadBytes => 1_024;
    /// <inheritdoc />
    public int Seed => 1_729;
    /// <inheritdoc />
    public int Dimensions => 32;
    /// <inheritdoc />
    public int TopK => 10;
    /// <inheritdoc />
    public int TimeoutSeconds => 30;
    /// <inheritdoc />
    public int GraphVertices => 0;
    /// <inheritdoc />
    public int GraphFanOut => 0;
    /// <inheritdoc />
    public int GraphDepth => 0;
}
