using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Identifies an operation capability that can be granted to a principal.</summary>
[Flags]
public enum Capability : long
{
    /// <summary>Grants no capabilities.</summary>
    None = 0,
    /// <summary>Allows reading documents.</summary>
    DocumentsRead = 1L << 0,
    /// <summary>Allows creating and changing documents.</summary>
    DocumentsWrite = 1L << 1,
    /// <summary>Allows query execution.</summary>
    Query = 1L << 2,
    /// <summary>Allows appending events.</summary>
    EventsAppend = 1L << 3,
    /// <summary>Allows reading events.</summary>
    EventsRead = 1L << 4,
    /// <summary>Allows publishing queue messages.</summary>
    QueuePublish = 1L << 5,
    /// <summary>Allows consuming queue messages.</summary>
    QueueConsume = 1L << 6,
    /// <summary>Allows acknowledging queue deliveries.</summary>
    QueueAck = 1L << 7,
    /// <summary>Allows renewing queue leases.</summary>
    QueueRenew = 1L << 8,
    /// <summary>Allows inspecting queue state.</summary>
    QueueInspect = 1L << 9,
    /// <summary>Allows reading dead-lettered messages.</summary>
    DeadLettersRead = 1L << 10,
    /// <summary>Allows redriving dead-lettered messages.</summary>
    DeadLettersRedrive = 1L << 11,
    /// <summary>Allows managing subscriptions.</summary>
    SubscriptionsManage = 1L << 12,
    /// <summary>Allows reading graph edges.</summary>
    GraphRead = 1L << 13,
    /// <summary>Allows changing graph edges.</summary>
    GraphWrite = 1L << 14,
    /// <summary>Allows reading time-series samples.</summary>
    SeriesRead = 1L << 15,
    /// <summary>Allows appending time-series samples.</summary>
    SeriesAppend = 1L << 16,
    /// <summary>Allows searching vector indexes.</summary>
    VectorSearch = 1L << 17,
    /// <summary>Allows raw vector access.</summary>
    VectorRaw = 1L << 18,
    /// <summary>Allows managing resource schemas.</summary>
    SchemaManage = 1L << 19,
    /// <summary>Allows managing principals and credentials.</summary>
    SecurityManage = 1L << 20,
    /// <summary>Allows managing backups.</summary>
    BackupManage = 1L << 21,
    /// <summary>Allows restoring backups.</summary>
    BackupRestore = 1L << 22,
    /// <summary>Allows diagnostic operations.</summary>
    Diagnose = 1L << 23,
    /// <summary>Allows exporting data.</summary>
    DataExport = 1L << 24,
    /// <summary>Allows publishing topic events.</summary>
    TopicsPublish = 1L << 25,
    /// <summary>Allows reading topic events.</summary>
    TopicsRead = 1L << 26,
    /// <summary>Allows consuming subscription deliveries.</summary>
    SubscriptionsConsume = 1L << 27,
    /// <summary>Allows acknowledging subscription deliveries.</summary>
    SubscriptionsAck = 1L << 28,
    /// <summary>Allows reading change feeds.</summary>
    ChangesRead = 1L << 29,
    /// <summary>Allows reading binary object metadata and ranges.</summary>
    BlobRead = 1L << 30,
    /// <summary>Allows scoped binary upload lifecycle and progress.</summary>
    BlobWrite = 1L << 31,
    /// <summary>Allows conditional binary object deletion.</summary>
    BlobDelete = 1L << 32,
    /// <summary>Allows bounded cleanup of eligible binary versions.</summary>
    BlobManage = 1L << 33,
    /// <summary>Allows advancing bounded time-series retention.</summary>
    SeriesManage = 1L << 34,
    /// <summary>Allows complete raw worker replay of event streams and derived state.</summary>
    EventsReplay = 1L << 35,
    /// <summary>Allows conditional replacement of bounded aggregate snapshots.</summary>
    EventsSnapshotsManage = 1L << 36,
    /// <summary>Allows managing and evaluating durable recurring schedules and saga timeouts.</summary>
    SchedulerManage = 1L << 37,
    /// <summary>Combines every defined capability.</summary>
    All = (1L << 38) - 1
}

/// <summary>Grants capabilities for a database and resource scope.</summary>
/// <param name="Database">Identifies the database scope.</param>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
/// <param name="Capabilities">Lists the granted operations.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ScopeGrant)]
public sealed record ScopeGrant([property: Orleans.Id(0)] string Database, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] Capability Capabilities);

/// <summary>Persists a principal identity, grants, and authorization policy state.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="TenantId">Identifies the owning tenant.</param>
/// <param name="Grants">Lists scoped capabilities.</param>
/// <param name="FieldGrants">Lists field-level grants.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PrincipalRecord)]
public sealed record PrincipalRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string TenantId, [property: Orleans.Id(2)] ImmutableArray<ScopeGrant> Grants, [property: Orleans.Id(3)] ImmutableArray<string> FieldGrants)
{
    /// <summary>Gets whether the principal has cluster administration rights.</summary>
    [Orleans.Id(4)]
    public bool ClusterAdministrator { get; init; }
    /// <summary>Gets the optional owner identity.</summary>
    [Orleans.Id(5)]
    public string? OwnerId { get; init; }
    /// <summary>Gets the projects visible to the principal.</summary>
    [Orleans.Id(6)]
    public ImmutableArray<string> Projects { get; init; } = [];
    /// <summary>Gets whether row-level restrictions are enabled.</summary>
    [Orleans.Id(7)]
    public bool RestrictRows { get; init; }
    /// <summary>Gets whether the principal is revoked.</summary>
    [Orleans.Id(8)]
    public bool Revoked { get; init; }
    /// <summary>Gets the optional expiration time.</summary>
    [Orleans.Id(9)]
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets the principal policy epoch.</summary>
    [Orleans.Id(10)]
    public long PolicyEpoch { get; init; } = 1;
}

/// <summary>Persists an API key verifier and its principal and lifecycle state.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PrincipalId">Identifies the principal receiving the delivery.</param>
/// <param name="Verifier">Stores the non-secret API key verifier.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="Revoked">Marks the identity as revoked when true.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ApiKeyRecord)]
public sealed record ApiKeyRecord([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PrincipalId, [property: Orleans.Id(2)] string Verifier, [property: Orleans.Id(3)] DateTimeOffset? ExpiresAt = null, [property: Orleans.Id(4)] bool Revoked = false);

/// <summary>Returns the identifier and one-time secret of a newly created API key.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="Secret">Contains the one-time API key secret.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ApiKeyCreated)]
public sealed record ApiKeyCreated([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string Secret);

/// <summary>Requests persistence of a principal configuration.</summary>
/// <param name="Principal">Specifies the principal value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigurePrincipalRequest)]
public sealed record ConfigurePrincipalRequest([property: Orleans.Id(0)] PrincipalRecord Principal);

/// <summary>Requests persistence of an API key configuration.</summary>
/// <param name="ApiKey">Specifies the api key value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureApiKeyRequest)]
public sealed record ConfigureApiKeyRequest([property: Orleans.Id(0)] ApiKeyRecord ApiKey);
