using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Persists Orleans membership through the node-owned replica quorum without constructing an Orleans client.</summary>
/// <param name="database">Canonical local database whose reads follow an established quorum barrier.</param>
/// <param name="coordinator">Admission and commit boundary for database membership control operations.</param>
/// <param name="endpoint">Local replica readiness; it owns neither this table's schema nor routing activations.</param>
/// <param name="clusterId">Exact cluster identity permitted to delete this table.</param>
/// <param name="internalPrincipal">Persisted trusted principal for internal membership mutations.</param>
/// <param name="clock">System clock for bounded provider calls and cancellable startup retry delays.</param>
/// <param name="startupCancellation">Silo startup cancellation, used only by membership initialization.</param>
public sealed class ReplicaMembershipTable(DatabaseEngine database, ICommitCoordinator coordinator, IReplicaEndpoint endpoint,
    string clusterId, string internalPrincipal, TimeProvider clock, CancellationToken startupCancellation) : IMembershipTable
{
    private readonly ReplicaMembershipStore store = new(database, coordinator, internalPrincipal);
    private readonly IReplicaEndpoint replica = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    private readonly string configuredCluster = ReplicaMembershipProtocol.ClusterIdentity(clusterId);
    private readonly TimeProvider time = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Waits for early local transport, then acquires quorum with cancellable transient-only retries.</summary>
    /// <param name="tryInitTableVersion">Native provider hint; the persisted table remains authoritative.</param>
    public async Task InitializeMembershipTable(bool tryInitTableVersion)
    {
        await replica.TransportReady.WaitAsync(startupCancellation).ConfigureAwait(false);
        while (true)
        {
            startupCancellation.ThrowIfCancellationRequested();
            try
            {
                await store.ReadAsync(startupCancellation).ConfigureAwait(false);
                return;
            }
            catch (KeyLoadException error) when (ReplicaMembershipProtocol.IsTransient(error))
            {
                await Task.Delay(ReplicaMembershipProtocol.StartupRetryDelay, time, startupCancellation).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Returns all native rows and their table version from one quorum-backed snapshot.</summary>
    /// <returns>The current membership rows and table version.</returns>
    public async Task<MembershipTableData> ReadAll()
    {
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        return (await store.ReadAsync(deadline.Token).ConfigureAwait(false)).Data();
    }

    /// <summary>Returns only the exact runtime address, including generation, with the current table version.</summary>
    /// <param name="key">The exact Orleans address to read.</param>
    /// <returns>The matching row, if present, and the current table version.</returns>
    public async Task<MembershipTableData> ReadRow(SiloAddress key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        return (await store.ReadAsync(deadline.Token).ConfigureAwait(false)).Data(key);
    }

    /// <summary>Atomically inserts a new row only when its address is absent and the table ETag matches.</summary>
    /// <param name="entry">The native membership row to insert.</param>
    /// <param name="tableVersion">The expected next table version.</param>
    /// <returns>True when the row was inserted by the successful compare-exchange.</returns>
    public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(tableVersion);
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        var snapshot = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
        var updated = snapshot.Insert(entry, tableVersion);
        return updated is not null && await store.CompareExchangeAsync(updated, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Atomically advances matching row and table ETags while preserving a newer persisted heartbeat.</summary>
    /// <param name="entry">The updated native membership row.</param>
    /// <param name="etag">The row ETag observed by the caller.</param>
    /// <param name="tableVersion">The expected next table version.</param>
    /// <returns>True when the row update was committed by compare-exchange.</returns>
    public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(tableVersion);
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        var snapshot = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
        var updated = snapshot.Update(entry, etag, tableVersion);
        return updated is not null && await store.CompareExchangeAsync(updated, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Retries bounded CAS contention while changing only a strictly newer heartbeat.</summary>
    /// <param name="entry">The native row containing the candidate heartbeat.</param>
    public async Task UpdateIAmAlive(MembershipEntry entry)
    {
        ReplicaMembershipProtocol.ValidateEntry(entry);
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        for (var attempt = 0; attempt < ReplicaMembershipProtocol.HeartbeatAttempts; attempt++)
        {
            var snapshot = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
            var updated = snapshot.Heartbeat(entry);
            if (updated is null || await store.CompareExchangeAsync(updated, deadline.Token).ConfigureAwait(false))
            {
                return;
            }
        }

        throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipProtocol.HeartbeatContention);
    }

    /// <summary>Deletes all rows only for the exact configured cluster, with persisted CAS conflict detection.</summary>
    /// <param name="requestedCluster">The cluster ID supplied by the membership provider.</param>
    public async Task DeleteMembershipTableEntries(string requestedCluster)
    {
        if (!string.Equals(requestedCluster, configuredCluster, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaMembershipProtocol.ClusterMismatch);
        }

        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        var snapshot = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
        if (!await store.CompareExchangeAsync(snapshot.Delete(), deadline.Token).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaMembershipProtocol.DeleteConflict);
        }
    }

    /// <summary>Attempts one CAS removal of dead rows whose heartbeat is older than the UTC cutoff.</summary>
    /// <param name="beforeDate">The UTC heartbeat cutoff for dead-row cleanup.</param>
    public async Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
    {
        using var deadline = ReplicaMembershipProtocol.Deadline(time);
        var snapshot = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
        var updated = snapshot.Cleanup(beforeDate);
        if (updated is not null)
        {
            await store.CompareExchangeAsync(updated, deadline.Token).ConfigureAwait(false);
        }
    }
}
