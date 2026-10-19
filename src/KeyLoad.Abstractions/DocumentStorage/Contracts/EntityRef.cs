using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Identifies a document or graph endpoint within an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Collection">Identifies the document collection.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.EntityRef)]
public sealed record EntityRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string Id)
{
    /// <summary>Gets the exact server-bound lifetime, or null only on a fresh external name-only submission.</summary>
    [Orleans.Id(EntityRefLifetimeContract.LifetimeField)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EntityRefLifetime? Lifetime
    {
        get; init;
    }
}
