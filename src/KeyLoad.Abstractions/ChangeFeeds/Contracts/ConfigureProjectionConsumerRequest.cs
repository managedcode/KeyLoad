namespace KeyLoad;

/// <summary>Requests creation or replacement of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to configure.</param>
/// <param name="Definition">The consumer definition.</param>
/// <param name="StartAfter">The optional outbox sequence after which consumption begins.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureProjectionConsumerRequest)]
public sealed record ConfigureProjectionConsumerRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] ProjectionConsumerDefinition Definition, [property: Orleans.Id(3)] long? StartAfter = null)
{
    /// <summary>Gets the original manual Build reserved atomically with initial configuration.</summary>
    [Orleans.Id(4), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AnnMaintenanceRequest? InitialAnnMaintenance
    {
        get; init;
    }
}
