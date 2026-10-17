namespace KeyLoad.Comparisons;

/// <summary>Describes a comparison target's version, topology, and operation contracts.</summary>
/// <param name="Name">The target name shown in comparison results.</param>
/// <param name="Version">The target version reported for this run.</param>
/// <param name="Topology">The target's reported topology description.</param>
/// <param name="WriteAcknowledgement">The target's write acknowledgement contract.</param>
/// <param name="ReadContract">The target's read consistency or visibility contract.</param>
/// <param name="Transport">The transport used to reach the target.</param>
/// <param name="Authorization">The authorization mode used for the target.</param>
/// <param name="Image">The container image reference, when the target uses a container.</param>
public sealed record TargetProfile(string Name, string Version, string Topology, string WriteAcknowledgement,
    string ReadContract, string Transport, string Authorization, string? Image)
{
    /// <summary>Gets or initializes evidence describing the observed target cluster, when available.</summary>
    public ClusterEvidence? Cluster
    {
        get; init;
    }
}
