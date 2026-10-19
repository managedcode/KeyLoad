using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines a named index over document fields.</summary>
/// <param name="Name">Provides the resource or index name.</param>
/// <param name="Fields">Lists indexed field paths.</param>
/// <param name="Unique">Requires indexed values to be unique when true.</param>
/// <param name="IncludeNull">Includes explicit null values when true.</param>
/// <param name="IncludeMissing">Includes missing field values when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.IndexDefinition)]
public sealed record IndexDefinition([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] ImmutableArray<string> Fields, [property: Orleans.Id(2)] bool Unique = false, [property: Orleans.Id(3)] bool IncludeNull = true, [property: Orleans.Id(4)] bool IncludeMissing = false);
