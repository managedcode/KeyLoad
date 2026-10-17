namespace KeyLoad;

/// <summary>Links two canonical entities in a queue payload or derived graph action.</summary>
/// <param name="From">Identifies the source entity.</param>
/// <param name="To">Identifies the target entity.</param>
/// <param name="Label">Names the graph relationship.</param>
/// <param name="AttributesJson">Contains authorized relationship attributes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueGraphLink)]
public sealed record QueueGraphLink([property: Orleans.Id(0)] EntityRef From,
    [property: Orleans.Id(1)] EntityRef To, [property: Orleans.Id(2)] string Label,
    [property: Orleans.Id(3)] string AttributesJson = DatabaseCompositionDefaults.EmptyAttributes);
