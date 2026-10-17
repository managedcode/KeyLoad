using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Selects one field path and assigns it an output alias.</summary>
/// <param name="Path">The field path to select.</param>
/// <param name="Alias">The name used for the selected value in the result.</param>
/// <param name="SourceAlias">The optional source alias used only by Q2 joined projections.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Selection)]
public sealed record Selection([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] string Alias,
    [property: Orleans.Id(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceAlias = null);
