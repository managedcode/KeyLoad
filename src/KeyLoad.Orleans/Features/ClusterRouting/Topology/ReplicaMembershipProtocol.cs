using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaMembershipProtocol
{
    internal const string TableKey = "orleans-membership";
    internal const string StorageSpace = "membership";
    internal const string HeartbeatContention = "Membership heartbeat contention exceeded its retry budget.";
    internal const string ClusterMismatch = "The cluster ID does not match.";
    internal const string DeleteConflict = "Membership changed during deletion.";
    internal const int HeartbeatAttempts = 16;
    internal static TimeSpan StartupRetryDelay { get; } = TimeSpan.FromMilliseconds(250);
    internal static TimeSpan StartupTimeout { get; } = TimeSpan.FromSeconds(120);
    internal static TimeSpan RequestTimeout { get; } = ReplicaProtocol.ReadBarrierTimeout + ReplicaProtocol.CommandTimeout;

    internal static CancellationTokenSource Deadline(TimeProvider clock) => new(RequestTimeout, clock);

    internal static bool IsTransient(KeyLoadException error) => error.Code is ErrorCode.OwnershipLost
        or ErrorCode.UnknownWriteOutcome or ErrorCode.ResourceExhausted;

    internal static string ClusterIdentity(string clusterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterId);
        return clusterId;
    }

    internal static void ValidateEntry(MembershipEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.SiloAddress);
    }
}
