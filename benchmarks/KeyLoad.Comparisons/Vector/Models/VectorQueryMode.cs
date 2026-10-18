using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The native vector query workload applied by a vector profile.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VectorQueryMode
{
    /// <summary>Searches the complete corpus without a predicate.</summary>
    Plain,
    /// <summary>Searches records whose number is divisible by 100.</summary>
    Filtered,
    /// <summary>Searches the stable 90 percent while updating excluded records.</summary>
    Mixed
}
