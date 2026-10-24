namespace KeyLoad;

/// <summary>Reports a projection consumer's definition and progress.</summary>
/// <param name="Consumer">The consumer identity.</param>
/// <param name="Definition">The active consumer definition.</param>
/// <param name="Checkpoint">The highest outbox sequence acknowledged by the consumer.</param>
/// <param name="Released">Whether the consumer has been released.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ProjectionConsumerInfo)]
public sealed record ProjectionConsumerInfo([property: Orleans.Id(0)] ProjectionConsumerRef Consumer, [property: Orleans.Id(1)] ProjectionConsumerDefinition Definition,
    [property: Orleans.Id(2)] long Checkpoint, [property: Orleans.Id(3)] bool Released)
{
    /// <summary>Gets the authenticated immutable original maintenance configuration authority.</summary>
    [Orleans.Id(5), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProjectionMaintenanceAuthority? MaintenanceAuthority
    {
        get; init;
    }

    /// <summary>Gets the immutable initial manual ANN reservation for this generation.</summary>
    [Orleans.Id(6), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AnnMaintenanceRequest? InitialAnnMaintenance
    {
        get; init;
    }
    /// <summary>Gets whether the exact initial execution full result was acknowledged by canonical settlement.</summary>
    [Orleans.Id(7), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool InitialAnnMaintenanceSettled
    {
        get; init;
    }

}
