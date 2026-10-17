using Orleans.Serialization.Codecs;

namespace KeyLoad.Replication;

internal sealed class ReplicaEntryArrayInspectionCodec(IFieldCodec<ReplicaEntry> elementCodec)
    : ReplicaArrayInspectionCodec<ReplicaEntry>(elementCodec);
