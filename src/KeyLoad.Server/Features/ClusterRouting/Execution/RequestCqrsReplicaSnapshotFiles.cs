using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Borrows the original private file lock, immutable ledger and configured quotas.</summary>
internal sealed class RequestCqrsReplicaSnapshotFiles(string root, string session, RequestCqrsProbeJson json,
    RequestCqrsProbeRecords records, IOptions<RequestProbeExecutionOptions> options, Lock sync,
    Func<RequestCqrsProbeSnapshot> snapshot, Action<string, byte[]> write)
{
    private const int NoMatches = 0;

    internal void Read(string path, string name, List<RequestCqrsReplicaSnapshotRecord> values, HashSet<string> present)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        var value = RequestCqrsReplicaSnapshotJson.Read(json, bytes);
        if (value.SessionId != session || RequestCqrsReplicaSnapshotJson.Name(value) != name)
        { throw RequestCqrsReplicaSnapshotJson.Invalid(); }
        records.RegisterImmutable(name, bytes);
        present.Add(name);
        values.Add(value);
    }

    internal void Publish(RequestCqrsProbeClaim claim, RequestCqrsReplicaSnapshotRecord value)
    {
        lock (sync)
        {
            RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot());
            if (value.SessionId != session || value.ArmId != claim.Arm.Record.ArmId
                || value.RequestId != claim.Identity.RequestId)
            { throw RequestCqrsReplicaSnapshotJson.Invalid(); }
            var name = RequestCqrsReplicaSnapshotJson.Name(value);
            var bytes = RequestCqrsReplicaSnapshotJson.Write(json, value);
            write(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal static void RequireInventory(List<RequestCqrsReplicaSnapshotRecord> values,
        List<RequestCqrsProbeLoadedArm> arms, List<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in values)
        {
            var matching = markers.Where(marker => marker.ArmId == value.ArmId && marker.RequestId == value.RequestId
                && marker.SessionId == value.SessionId && marker.Phase == RequestCqrsProbePhase.ReplicaSnapshotReadiness).ToArray();
            if (matching.Length != NoMatches)
            { continue; }
            // Atomic sidecar publication precedes Observed; this transient file cannot qualify fixture readiness.
            if (!arms.Any(arm => arm.Record.ArmId == value.ArmId && arm.Record.SessionId == value.SessionId
                && arm.Record.ReadKind == GrainReadKind.NodeStatus && arm.Record.CommandId == Guid.Empty
                && arm.Record.Phase == RequestCqrsProbePhase.ReplicaSnapshotReadiness
                && arm.Record.Action == RequestCqrsProbeAction.Hold))
            { throw RequestCqrsReplicaSnapshotJson.Invalid(); }
        }
    }
}
