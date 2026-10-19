namespace KeyLoad;

/// <summary>Requests persistence of an API key configuration.</summary>
/// <param name="ApiKey">Specifies the api key value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ConfigureApiKeyRequest)]
public sealed record ConfigureApiKeyRequest([property: Orleans.Id(0)] ApiKeyRecord ApiKey);
