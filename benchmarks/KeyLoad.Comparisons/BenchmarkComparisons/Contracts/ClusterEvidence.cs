using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Records the observed cluster shape and state for a target profile.</summary>
/// <param name="Nodes">The number of nodes reported for the cluster.</param>
/// <param name="DataCopies">The number of data copies reported by the target.</param>
/// <param name="State">The target-reported cluster state.</param>
/// <param name="Observations">Additional target-reported cluster observations.</param>
public sealed record ClusterEvidence(int Nodes, int DataCopies, string State,
    [property: JsonRequired] ImmutableArray<string> Observations);
