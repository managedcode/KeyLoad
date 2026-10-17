namespace KeyLoad;

/// <summary>Retains the immutable identity of an active resource's original canonical creation.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceLifetimeContract.LifetimeAlias)]
internal sealed record ResourceLifetimeV1(
    [property: Orleans.Id(ResourceLifetimeContract.TenantField)] string TenantId,
    [property: Orleans.Id(ResourceLifetimeContract.DatabaseField)] string DatabaseId,
    [property: Orleans.Id(ResourceLifetimeContract.ResourceField)] string ResourceName,
    [property: Orleans.Id(ResourceLifetimeContract.TransactionDomainField)] string TransactionDomainId,
    [property: Orleans.Id(ResourceLifetimeContract.KindField)] ResourceKind Kind,
    [property: Orleans.Id(ResourceLifetimeContract.IncarnationField)] ResourceIncarnationV1 Incarnation,
    [property: Orleans.Id(ResourceLifetimeContract.CreationFingerprintField)] string CreationFingerprint);
