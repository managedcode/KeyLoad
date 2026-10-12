using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record NativeBlobWholeOwnerCut(ReplicaSiloDiscovery Owner, Guid CanonicalNodeId,
    Guid ReplicaNodeId, long CanonicalPosition, long ReplicaPosition, string[] CanonicalRows,
    string[] ReplicaRows, long Applied, ReplicaHardState HardState, ReplicaEntry[] Entries)
{
    public override string ToString() => "NativeBlobWholeOwnerCut(<private>)";

    internal PartitionMovementBlobWireNativeCut OriginalCut => new(new(Owner.VoterId,
        CanonicalPosition, CanonicalRows, null, null, null, null, null, null, null, null,
        [], null, 0, null, 0), Applied);
}
