using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Exact scalar types supported by the typed relational row schema.</summary>
public enum RelationalColumnType
{
    /// <summary>A JSON string.</summary>
    Text,
    /// <summary>A JSON boolean.</summary>
    Boolean,
    /// <summary>An exact signed 64-bit JSON integer.</summary>
    WholeNumber,
    /// <summary>A JSON number representable as a CLR decimal.</summary>
    FixedPoint,
    /// <summary>An ISO timestamp with an explicit UTC offset.</summary>
    UtcTimestamp
}

/// <summary>Defines one top-level typed row column.</summary>
/// <param name="Name">Case-sensitive column identity.</param>
/// <param name="Type">Exact scalar type.</param>
/// <param name="Nullable">Whether absent and explicit null values are allowed.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.RelationalColumn)]
public sealed record RelationalColumn([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] RelationalColumnType Type, [property: Orleans.Id(2)] bool Nullable = false);

/// <summary>Constrains canonical entity rows without introducing another storage engine.</summary>
/// <param name="PrimaryKey">Required nonnullable Text column matching EntityRef.Id.</param>
/// <param name="Columns">Closed immutable top-level column definitions.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.RelationalSchema)]
public sealed record RelationalSchema([property: Orleans.Id(0)] string PrimaryKey, [property: Orleans.Id(1)] ImmutableArray<RelationalColumn> Columns);
