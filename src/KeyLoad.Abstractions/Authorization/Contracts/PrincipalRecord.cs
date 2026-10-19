using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Persists a principal identity, grants, and authorization policy state.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="Grants">Lists scoped capabilities.</param>
/// <param name="FieldGrants">Lists field-level grants.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PrincipalRecord)]
public sealed record PrincipalRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string TenantId, [property: Orleans.Id(2)] ImmutableArray<ScopeGrant> Grants, [property: Orleans.Id(3)] ImmutableArray<string> FieldGrants)
{
    private const int InitialPolicyEpoch = 1;

    /// <summary>Gets whether the principal has cluster administration rights.</summary>
    [Orleans.Id(4)]
    public bool ClusterAdministrator
    {
        get; init;
    }
    /// <summary>Gets the optional owner identity.</summary>
    [Orleans.Id(5)]
    public string? OwnerId
    {
        get; init;
    }
    /// <summary>Gets the projects visible to the principal.</summary>
    [Orleans.Id(6)]
    public ImmutableArray<string> Projects { get; init; } = [];
    /// <summary>Gets whether row-level restrictions are enabled.</summary>
    [Orleans.Id(7)]
    public bool RestrictRows
    {
        get; init;
    }
    /// <summary>Gets whether the principal is revoked.</summary>
    [Orleans.Id(8)]
    public bool Revoked
    {
        get; init;
    }
    /// <summary>Gets the optional expiration time.</summary>
    [Orleans.Id(9)]
    public DateTimeOffset? ExpiresAt
    {
        get; init;
    }
    /// <summary>Gets the principal policy epoch.</summary>
    [Orleans.Id(10)]
    public long PolicyEpoch { get; init; } = InitialPolicyEpoch;
    /// <summary>Gets the tenant-scoped teams declared by persisted administration.</summary>
    [Orleans.Id(11)]
    public ImmutableArray<string> Teams { get; init; } = [];
}
