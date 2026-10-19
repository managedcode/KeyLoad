using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Describes the resource effect produced by one committed mutation.</summary>
/// <param name="Kind">Identifies the mutation or trusted operation kind.</param>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.MutationReceipt)]
public sealed record MutationReceipt([property: Orleans.Id(0)] string Kind, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] string Id, [property: Orleans.Id(3)] long Revision)
{
    [NonSerialized]
    private ImmutableArray<EntityRef> compositionReferences;

    [JsonIgnore]
    [Orleans.Id(4)]
    internal ImmutableArray<EntityRef> CompositionReferences
    {
        get => compositionReferences.IsDefault ? [] : compositionReferences;
        set => compositionReferences = value;
    }
}
