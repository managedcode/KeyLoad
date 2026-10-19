namespace KeyLoad;

/// <summary>Returns the identifier and one-time secret of a newly created API key.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Secret">Contains the one-time API key secret.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ApiKeyCreated)]
public sealed record ApiKeyCreated([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string Secret);
