using System.ComponentModel;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Describes the deployment topology requested for a comparison target.</summary>
[TypeConverter(typeof(ComparisonTopologyConverter))]
public enum ComparisonTopology
{
    /// <summary>Requests a standalone external-engine profile; the KeyLoad target retains its required RF3 topology.</summary>
    [JsonStringEnumMemberName(ComparisonTopologyNames.Single)]
    Standalone,
    /// <summary>Uses a replicated target topology and records its cluster evidence.</summary>
    Replicated
}
