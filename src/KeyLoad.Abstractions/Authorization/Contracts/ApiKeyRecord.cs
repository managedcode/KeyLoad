namespace KeyLoad;

/// <summary>Persists an API key verifier and its principal and lifecycle state.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="Verifier">Stores the non-secret API key verifier.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="Revoked">Marks the identity as revoked when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ApiKeyRecord)]
public sealed record ApiKeyRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PrincipalId, [property: Orleans.Id(2)] string Verifier, [property: Orleans.Id(3)] DateTimeOffset? ExpiresAt = null, [property: Orleans.Id(4)] bool Revoked = false);
