using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The closed vector workload axes admitted by native qualification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorIndexKind
{
    /// <summary>Uses native exact vector distance ordering without an ANN index.</summary>
    Exact,
    /// <summary>Uses a Hierarchical Navigable Small World index.</summary>
    Hnsw,
    /// <summary>Uses an inverted-file flat index.</summary>
    IvfFlat,
    /// <summary>Uses a database's native ANN method without relabeling its algorithm.</summary>
    NativeAnn
}
