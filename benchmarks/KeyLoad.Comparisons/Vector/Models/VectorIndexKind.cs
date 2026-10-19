using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The closed vector workload axes admitted by execution qualification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorIndexKind
{
    /// <summary>Uses execution exact vector distance ordering without an ANN index.</summary>
    Exact,
    /// <summary>Uses a Hierarchical Navigable Small World index.</summary>
    Hnsw,
    /// <summary>Uses an inverted-file flat index.</summary>
    IvfFlat,
    /// <summary>Uses a database's execution ANN method without relabeling its algorithm.</summary>
    RuntimeAnn
}
