using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains a page of query rows and its continuation metadata.</summary>
/// <param name="Rows">The rows in this page.</param>
/// <param name="Cursor">The cursor for the next page, if one exists.</param>
/// <param name="CutPosition">The committed position at which the query snapshot was taken.</param>
/// <param name="AccessPath">The access path used to execute the query.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryPage)]
public sealed record QueryPage([property: Orleans.Id(0)] ImmutableArray<QueryRow> Rows, [property: Orleans.Id(1)] string? Cursor, [property: Orleans.Id(2)] long CutPosition, [property: Orleans.Id(3)] string AccessPath);
