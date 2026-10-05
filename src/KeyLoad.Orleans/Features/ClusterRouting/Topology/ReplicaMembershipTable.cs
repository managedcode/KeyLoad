using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Persists Orleans membership through the node-owned replica quorum without constructing an Orleans client.</summary>
public sealed class ReplicaMembershipTable : IMembershipTable
{
    private readonly ReplicaMembershipStore store;
    private readonly ReplicaConsensus replica;
    private readonly string configuredCluster;
    private readonly TimeProvider time;
    private readonly CancellationToken startupCancellation;
    private readonly int maximumRows;

    /// <summary>Creates the standard unbounded local membership provider.</summary>
    /// <param name="database">Canonical local database whose reads follow an established quorum barrier.</param>
    /// <param name="coordinator">Admission and commit boundary for database membership control operations.</param>
    /// <param name="endpoint">Actual node-owned consensus for readiness and trusted native control read cuts.</param>
    /// <param name="clusterId">Exact cluster identity permitted to delete this table.</param>
    /// <param name="internalPrincipal">Persisted trusted principal for internal membership mutations.</param>
    /// <param name="clock">System clock for bounded provider calls and cancellable startup retry delays.</param>
    /// <param name="startupCancellation">Silo startup cancellation, used only by membership initialization.</param>
    public ReplicaMembershipTable(DatabaseEngine database, ICommitCoordinator coordinator, ReplicaConsensus endpoint,
        string clusterId, string internalPrincipal, TimeProvider clock, CancellationToken startupCancellation)
        : this(database, coordinator, endpoint, clusterId, internalPrincipal, clock, 0, startupCancellation) { }

    /// <summary>Creates a profile-bounded authority provider without changing the default provider contract.</summary>
    internal ReplicaMembershipTable(DatabaseEngine database, ICommitCoordinator coordinator, ReplicaConsensus endpoint,
        string clusterId, string internalPrincipal, TimeProvider clock, int maximumRows, CancellationToken startupCancellation)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        replica = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        configuredCluster = ReplicaMembershipProtocol.ClusterIdentity(clusterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(internalPrincipal);
        time = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        this.startupCancellation = startupCancellation;
        this.maximumRows = maximumRows;
        store = new(database, coordinator, endpoint, internalPrincipal, maximumRows);
    }

    /// <inheritdoc />
    public Task InitializeMembershipTable(bool tryInitTableVersion)
        => InitializeMembershipTableAsync(tryInitTableVersion, CancellationToken.None);

    /// <summary>Waits for early local transport, then acquires quorum within the startup lifetime.</summary>
    /// <param name="tryInitTableVersion">Native provider hint; persisted state remains authoritative.</param>
    /// <param name="cancellationToken">Native Orleans startup cancellation.</param>
    public async Task InitializeMembershipTableAsync(bool tryInitTableVersion, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.StartupTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(startupCancellation, cancellationToken, deadline.Token);
        await replica.TransportReady.WaitAsync(linked.Token).ConfigureAwait(false);
        while (true)
        {
            linked.Token.ThrowIfCancellationRequested();
            try
            {
                await store.ReadAsync(linked.Token).ConfigureAwait(false);
                return;
            }
            catch (KeyLoadException error) when (error.Code is ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome
                || maximumRows == 0 && error.Code == ErrorCode.ResourceExhausted)
            {
                await Task.Delay(ReplicaMembershipProtocol.StartupRetryDelay, time, linked.Token).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task<MembershipTableData> ReadAll() => ReadAllAsync(CancellationToken.None);

    /// <summary>Returns all native rows and their table version from one quorum-backed snapshot.</summary>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task<MembershipTableData> ReadAllAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return (await store.ReadAsync(linked.Token).ConfigureAwait(false)).Data();
    }

    /// <inheritdoc />
    public Task<MembershipTableData> ReadRow(SiloAddress key) => ReadRowAsync(key, CancellationToken.None);

    /// <summary>Returns only the exact runtime address, including generation, with the current table version.</summary>
    /// <param name="key">The exact Orleans address to read.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task<MembershipTableData> ReadRowAsync(SiloAddress key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        return (await store.ReadAsync(linked.Token).ConfigureAwait(false)).Data(key);
    }

    /// <inheritdoc />
    public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
        => InsertRowAsync(entry, tableVersion, CancellationToken.None);

    /// <summary>Atomically inserts a new row only when its address is absent and table ETag matches.</summary>
    /// <param name="entry">The native membership row to insert.</param>
    /// <param name="tableVersion">The expected next table version.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task<bool> InsertRowAsync(MembershipEntry entry, TableVersion tableVersion, CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(tableVersion);
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var snapshot = await store.ReadAsync(linked.Token).ConfigureAwait(false);
        var updated = snapshot.Insert(entry, tableVersion, maximumRows);
        return updated is not null && await store.CompareExchangeAsync(updated, linked.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
        => UpdateRowAsync(entry, etag, tableVersion, CancellationToken.None);

    /// <summary>Atomically updates an existing exact row while preserving a newer heartbeat.</summary>
    /// <param name="entry">The native membership row to update.</param>
    /// <param name="etag">The row ETag observed by the caller.</param>
    /// <param name="tableVersion">The expected next table version.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task<bool> UpdateRowAsync(MembershipEntry entry, string etag, TableVersion tableVersion,
        CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(tableVersion);
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var snapshot = await store.ReadAsync(linked.Token).ConfigureAwait(false);
        var updated = snapshot.Update(entry, etag, tableVersion, maximumRows);
        return updated is not null && await store.CompareExchangeAsync(updated, linked.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateIAmAlive(MembershipEntry entry) => UpdateIAmAliveAsync(entry, CancellationToken.None);

    /// <summary>Retries bounded CAS contention while changing only a strictly newer heartbeat.</summary>
    /// <param name="entry">The native row containing the candidate heartbeat.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task UpdateIAmAliveAsync(MembershipEntry entry, CancellationToken cancellationToken)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        for (var attempt = 0; attempt < ReplicaMembershipProtocol.HeartbeatAttempts; attempt++)
        {
            var snapshot = await store.ReadAsync(linked.Token).ConfigureAwait(false);
            var updated = snapshot.Heartbeat(entry, maximumRows);
            if (updated is null || await store.CompareExchangeAsync(updated, linked.Token).ConfigureAwait(false))
            {
                return;
            }
        }

        throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipProtocol.HeartbeatContention);
    }

    /// <inheritdoc />
    public Task DeleteMembershipTableEntries(string requestedCluster)
        => DeleteMembershipTableEntriesAsync(requestedCluster, CancellationToken.None);

    /// <summary>Deletes all rows only for the exact configured cluster, with persisted CAS conflict detection.</summary>
    /// <param name="requestedCluster">The cluster ID supplied by the membership provider.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task DeleteMembershipTableEntriesAsync(string requestedCluster, CancellationToken cancellationToken)
    {
        if (!string.Equals(requestedCluster, configuredCluster, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaMembershipProtocol.ClusterMismatch);
        }

        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var snapshot = await store.ReadAsync(linked.Token).ConfigureAwait(false);
        if (!await store.CompareExchangeAsync(snapshot.Delete(), linked.Token).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaMembershipProtocol.DeleteConflict);
        }
    }

    /// <inheritdoc />
    public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
        => CleanupDefunctSiloEntriesAsync(beforeDate, CancellationToken.None);

    /// <summary>Attempts one CAS removal of dead rows whose heartbeat is older than the UTC cutoff.</summary>
    /// <param name="beforeDate">The UTC heartbeat cutoff for dead-row cleanup.</param>
    /// <param name="cancellationToken">Bounds the provider operation and its native storage work.</param>
    public async Task CleanupDefunctSiloEntriesAsync(DateTimeOffset beforeDate, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(ReplicaMembershipProtocol.RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var snapshot = await store.ReadAsync(linked.Token).ConfigureAwait(false);
        var updated = snapshot.Cleanup(beforeDate, maximumRows);
        if (updated is not null)
        {
            await store.CompareExchangeAsync(updated, linked.Token).ConfigureAwait(false);
        }
    }
}
