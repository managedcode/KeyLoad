using KeyLoad.Replication;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed record NativeSubmissionJournalCut(Guid NodeId, Guid Incarnation, long Position,
    byte[] NativeHardState, byte[] NativeEntry, ReplicaHardState State, ReplicaEntry Entry)
{
    private const string RedactedDisplay = nameof(NativeSubmissionJournalCut) + "(<private>)";
    public override string ToString() => RedactedDisplay;
}
