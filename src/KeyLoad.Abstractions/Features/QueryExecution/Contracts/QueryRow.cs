using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Represents one query result row.</summary>
/// <param name="EntityId">The entity identifier.</param>
/// <param name="Revision">The entity revision represented by this row.</param>
/// <param name="Json">The serialized row value.</param>
/// <param name="Redacted">Whether one or more values were redacted.</param>
/// <param name="RedactedFields">The field paths whose values were redacted.</param>
/// <param name="Sources">Optional source identities for a Q2 joined pair.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRow)]
public sealed record QueryRow([property: Orleans.Id(0)] string EntityId, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] bool Redacted = false, [property: Orleans.Id(4)] ImmutableArray<string>? RedactedFields = null,
    [property: Orleans.Id(5), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<QueryRowSource>? Sources = null);

/// <summary>Identifies one source row contributing to a joined query result.</summary>
/// <param name="Alias">The declared SQL source alias.</param>
/// <param name="EntityId">The canonical entity identifier.</param>
/// <param name="Revision">The committed source revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRowSource)]
public sealed record QueryRowSource([property: Orleans.Id(0)] string Alias, [property: Orleans.Id(1)] string EntityId, [property: Orleans.Id(2)] long Revision);
