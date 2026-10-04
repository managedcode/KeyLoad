using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Represents one query result row.</summary>
/// <param name="EntityId">The entity identifier.</param>
/// <param name="Revision">The entity revision represented by this row.</param>
/// <param name="Json">The serialized row value.</param>
/// <param name="Redacted">Whether one or more values were redacted.</param>
/// <param name="RedactedFields">The field paths whose values were redacted.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRow)]
public sealed record QueryRow([property: Orleans.Id(0)] string EntityId, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] bool Redacted = false, [property: Orleans.Id(4)] ImmutableArray<string>? RedactedFields = null);
