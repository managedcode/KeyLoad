using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal static class PartitionHostReplicaLogObservation
{
    internal static void Observe(ReplicaCrashBoundary boundary, DurableReplicaLog log,
        RequestCqrsCanonicalApplyBridge probe)
    {
        if (boundary == ReplicaCrashBoundary.EntryAcknowledged && log.ReadEntry(log.State.LastIndex) is { } entry)
        {
            probe.ObserveReplicaEntry(entry);
        }
    }
}
