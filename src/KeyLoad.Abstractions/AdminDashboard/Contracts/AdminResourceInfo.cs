namespace KeyLoad;

/// <summary>Nonsecret resource metadata without grants, field policies or credential data.</summary>
/// <param name="Name">Persisted resource name.</param>
/// <param name="Kind">Resource capability family.</param>
/// <param name="TransactionDomainId">Persisted atomic transaction domain, distinct from partition key.</param>
/// <param name="SchemaVersion">Current resource schema revision.</param>
/// <param name="IndexCount">Number of configured resource indexes.</param>
/// <param name="Paused">Persisted dispatch pause state.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminResourceInfo)]
public sealed record AdminResourceInfo([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] ResourceKind Kind, [property: Orleans.Id(2)] string TransactionDomainId,
    [property: Orleans.Id(3)] long SchemaVersion, [property: Orleans.Id(4)] int IndexCount, [property: Orleans.Id(5)] bool Paused);
