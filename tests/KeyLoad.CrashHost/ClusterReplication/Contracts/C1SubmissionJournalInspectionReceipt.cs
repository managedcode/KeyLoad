using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed record C1SubmissionJournalInspectionReceipt(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid CanonicalNodeId,
    [property: JsonRequired] Guid ReplicaNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] long CanonicalPosition,
    [property: JsonRequired] long ReplicaPosition,
    [property: JsonRequired] byte[] NativeHardState,
    [property: JsonRequired] byte[] NativeEntry)
{
    public override string ToString() => nameof(C1SubmissionJournalInspectionReceipt);
}
