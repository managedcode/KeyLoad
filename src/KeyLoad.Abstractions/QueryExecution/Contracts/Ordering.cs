using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Specifies the ordering of query rows by one field path.</summary>
/// <param name="Path">The field path used for ordering.</param>
/// <param name="Descending">Whether to order from greatest to least.</param>
/// <param name="NullsFirst"></param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.Ordering)]
public sealed record Ordering([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] bool Descending,
    [property: Orleans.Id(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NullsFirst = null);
