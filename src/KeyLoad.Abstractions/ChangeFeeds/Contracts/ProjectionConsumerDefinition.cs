using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the indexes and mutation kinds used by a projection consumer.</summary>
/// <param name="IndexGeneration">The index generation required by the consumer.</param>
/// <param name="Resources">The resources consumed by the projection.</param>
/// <param name="MutationKinds">The mutation kinds consumed by the projection.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionConsumerDefinition)]
public sealed record ProjectionConsumerDefinition([property: Orleans.Id(0)] long IndexGeneration, [property: Orleans.Id(1)] ImmutableArray<string> Resources, [property: Orleans.Id(2)] ImmutableArray<string> MutationKinds)
{
    /// <summary>Gets the explicit native committed-vector maintenance selector.</summary>
    [Orleans.Id(3), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AnnProjectionMaintenanceProfile? AnnMaintenance
    {
        get; init;
    }
}
