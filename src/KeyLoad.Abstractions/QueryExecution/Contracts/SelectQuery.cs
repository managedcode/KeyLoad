using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Represents a bounded query plan as a typed abstract syntax tree.</summary>
/// <param name="Collection">The collection to query.</param>
/// <param name="Alias">The optional collection alias.</param>
/// <param name="Projection">The selected fields and aliases.</param>
/// <param name="Filter">The optional predicate applied to rows.</param>
/// <param name="Order">The requested ordering terms.</param>
/// <param name="Limit">The maximum number of rows to return.</param>
/// <param name="Explain">Whether to return query plan information.</param>
/// <param name="ModelSource">Optional read-only event or queue source binding.</param>
/// <param name="InnerJoin">Optional Q2 typed-row INNER JOIN.</param>
/// <param name="Offset"></param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SelectQuery)]
public sealed record SelectQuery([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string? Alias, [property: Orleans.Id(2)] ImmutableArray<Selection> Projection, [property: Orleans.Id(3)] Predicate? Filter,
    [property: Orleans.Id(4)] ImmutableArray<Ordering> Order, [property: Orleans.Id(5)] int Limit, [property: Orleans.Id(6)] bool Explain = false,
    [property: Orleans.Id(7), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModelQuerySource? ModelSource = null,
    [property: Orleans.Id(8), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] InnerJoinClause? InnerJoin = null,
    [property: Orleans.Id(9), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Offset = default);
