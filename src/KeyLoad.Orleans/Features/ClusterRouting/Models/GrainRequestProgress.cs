namespace KeyLoad.Orleans;

/// <summary>Identifies a verified request which has begun routing without asserting authorization or commit.</summary>
/// <param name="RequestId">The independently keyed request actor.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainRoutingProtocol.ProgressAlias)]
public sealed record GrainRequestProgress([property: global::Orleans.Id(0)] Guid RequestId)
{
    /// <summary>Gets the completed bounded administrative ANN phase, when applicable.</summary>
    [global::Orleans.Id(1), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AnnMaintenancePhase? AnnPhase { get; init; }
}
