using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests an administrator-authorized database catalog metadata page.</summary>
/// <param name="TenantId">Explicit persisted catalog tenant scope.</param>
/// <param name="DatabaseId">Explicit persisted catalog database scope.</param>
/// <param name="AfterName">Exclusive resource-name continuation, or null for the first page.</param>
/// <param name="Limit">Maximum returned metadata items.</param>
public sealed record AdminResourcesRequest(string TenantId, string DatabaseId, string? AfterName = null,
    int Limit = AdminDashboardProtocol.DefaultPageSize);

/// <summary>Nonsecret resource metadata without grants, field policies or credential data.</summary>
/// <param name="Name">Persisted resource name.</param>
/// <param name="Kind">Resource capability family.</param>
/// <param name="TransactionDomainId">Persisted atomic transaction domain, distinct from partition key.</param>
/// <param name="SchemaVersion">Current resource schema revision.</param>
/// <param name="IndexCount">Number of configured resource indexes.</param>
/// <param name="Paused">Persisted dispatch pause state.</param>
public sealed record AdminResourceInfo(string Name, ResourceKind Kind, string TransactionDomainId,
    long SchemaVersion, int IndexCount, bool Paused);

/// <summary>Bounded catalog page from one gated quorum-established read cut.</summary>
/// <param name="Items">Resource metadata in canonical key order.</param>
/// <param name="NextAfterName">Exclusive resource continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
public sealed record AdminResourcesPage(ImmutableArray<AdminResourceInfo> Items, string? NextAfterName, long CutPosition);

/// <summary>Requests a non-consuming administrator view of one queue lane.</summary>
/// <param name="Lane">Full atomic queue identity.</param>
/// <param name="AfterId">Exclusive metadata identifier continuation.</param>
/// <param name="Limit">Maximum returned metadata records.</param>
public sealed record AdminQueueRequest(QueueLaneRef Lane, string? AfterId = null,
    int Limit = AdminDashboardProtocol.DefaultPageSize);

/// <summary>Queue lifecycle metadata without payloads, headers, lease owners or delivery tokens.</summary>
/// <param name="Id">Message identifier.</param>
/// <param name="State">Persisted lifecycle; this observation does not sweep expiry.</param>
/// <param name="Attempts">Persisted delivery attempt count.</param>
/// <param name="StateVersion">Persisted lifecycle revision.</param>
/// <param name="NotBefore">Optional persisted schedule time.</param>
/// <param name="ExpiresAt">Optional persisted expiry time.</param>
/// <param name="LeaseUntil">Optional persisted lease expiry, without ownership authority.</param>
public sealed record AdminQueueItem(string Id, MessageState State, int Attempts, long StateVersion,
    DateTimeOffset? NotBefore, DateTimeOffset? ExpiresAt, DateTimeOffset? LeaseUntil);

/// <summary>Exact persisted queue counters plus a bounded metadata display page.</summary>
/// <param name="Counters">Persisted storage/in-flight counters; no inferred ready/DLQ total.</param>
/// <param name="Items">Queue metadata records in canonical key order.</param>
/// <param name="NextAfterId">Exclusive metadata continuation, null on exhaustion.</param>
/// <param name="CutPosition">Applied position of the gated read cut.</param>
public sealed record AdminQueuePage(QueueCounters Counters, ImmutableArray<AdminQueueItem> Items,
    string? NextAfterId, long CutPosition);
