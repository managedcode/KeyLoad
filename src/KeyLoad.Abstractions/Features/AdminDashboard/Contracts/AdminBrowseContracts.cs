using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests an administrator-authorized database catalog metadata page.</summary>
/// <param name="TenantId">Explicit persisted catalog tenant scope.</param>
/// <param name="DatabaseId">Explicit persisted catalog database scope.</param>
/// <param name="AfterName">Exclusive resource-name continuation, or null for the first page.</param>
/// <param name="Limit">Maximum returned metadata items.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminResourcesRequest)]
public sealed record AdminResourcesRequest([property: Orleans.Id(0)] string TenantId, [property: Orleans.Id(1)] string DatabaseId, [property: Orleans.Id(2)] string? AfterName = null,
    [property: Orleans.Id(3)] int Limit = AdminDashboardProtocol.DefaultPageSize);

/// <summary>Nonsecret resource metadata without grants, field policies or credential data.</summary>
/// <param name="Name">Persisted resource name.</param>
/// <param name="Kind">Resource capability family.</param>
/// <param name="TransactionDomainId">Persisted atomic transaction domain, distinct from partition key.</param>
/// <param name="SchemaVersion">Current resource schema revision.</param>
/// <param name="IndexCount">Number of configured resource indexes.</param>
/// <param name="Paused">Persisted dispatch pause state.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminResourceInfo)]
public sealed record AdminResourceInfo([property: Orleans.Id(0)] string Name, [property: Orleans.Id(1)] ResourceKind Kind, [property: Orleans.Id(2)] string TransactionDomainId,
    [property: Orleans.Id(3)] long SchemaVersion, [property: Orleans.Id(4)] int IndexCount, [property: Orleans.Id(5)] bool Paused);

/// <summary>Bounded catalog page from one gated quorum-established read cut.</summary>
/// <param name="Items">Resource metadata in canonical key order.</param>
/// <param name="NextAfterName">Exclusive resource continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminResourcesPage)]
public sealed record AdminResourcesPage([property: Orleans.Id(0)] ImmutableArray<AdminResourceInfo> Items, [property: Orleans.Id(1)] string? NextAfterName, [property: Orleans.Id(2)] long CutPosition);

/// <summary>Requests a non-consuming administrator view of one queue lane.</summary>
/// <param name="Lane">Full atomic queue identity.</param>
/// <param name="AfterId">Exclusive metadata identifier continuation.</param>
/// <param name="Limit">Maximum returned metadata records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminQueueRequest)]
public sealed record AdminQueueRequest([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string? AfterId = null,
    [property: Orleans.Id(2)] int Limit = AdminDashboardProtocol.DefaultPageSize);

/// <summary>Queue lifecycle metadata without payloads, headers, lease owners or delivery tokens.</summary>
/// <param name="Id">Message identifier.</param>
/// <param name="State">Persisted lifecycle; this observation does not sweep expiry.</param>
/// <param name="Attempts">Persisted delivery attempt count.</param>
/// <param name="StateVersion">Persisted lifecycle revision.</param>
/// <param name="NotBefore">Optional persisted schedule time.</param>
/// <param name="ExpiresAt">Optional persisted expiry time.</param>
/// <param name="LeaseUntil">Optional persisted lease expiry, without ownership authority.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminQueueItem)]
public sealed record AdminQueueItem([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] MessageState State, [property: Orleans.Id(2)] int Attempts, [property: Orleans.Id(3)] long StateVersion,
    [property: Orleans.Id(4)] DateTimeOffset? NotBefore, [property: Orleans.Id(5)] DateTimeOffset? ExpiresAt, [property: Orleans.Id(6)] DateTimeOffset? LeaseUntil);

/// <summary>Exact persisted queue counters plus a bounded metadata display page.</summary>
/// <param name="Counters">Persisted storage/in-flight counters; no inferred ready/DLQ total.</param>
/// <param name="Items">Queue metadata records in canonical key order.</param>
/// <param name="NextAfterId">Exclusive metadata continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminQueuePage)]
public sealed record AdminQueuePage([property: Orleans.Id(0)] QueueCounters Counters, [property: Orleans.Id(1)] ImmutableArray<AdminQueueItem> Items,
    [property: Orleans.Id(2)] string? NextAfterId, [property: Orleans.Id(3)] long CutPosition);
