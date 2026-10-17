namespace KeyLoad;

/// <summary>Defines one top-level typed row column.</summary>
/// <param name="Name">Case-sensitive column identity.</param>
/// <param name="Type">Exact scalar type.</param>
/// <param name="Nullable">Whether absent and explicit null values are allowed.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.RelationalColumn)]
public sealed record RelationalColumn([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] RelationalColumnType Type, [property: Orleans.Id(2)] bool Nullable = false);
