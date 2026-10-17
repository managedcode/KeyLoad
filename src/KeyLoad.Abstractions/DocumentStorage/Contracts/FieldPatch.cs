namespace KeyLoad;

/// <summary>Describes a set or removal operation for one document field path.</summary>
/// <param name="Path">Identifies a field path.</param>
/// <param name="Kind">Identifies the trusted operation kind.</param>
/// <param name="ValueJson">Contains the JSON value for a set operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.FieldPatch)]
public sealed record FieldPatch([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] PatchKind Kind, [property: Orleans.Id(2)] string? ValueJson = null);
