using Orleans.Serialization.Codecs;

namespace KeyLoad.Replication;

internal sealed class ReplicaVoterArrayInspectionCodec(IFieldCodec<string> elementCodec)
    : ReplicaArrayInspectionCodec<string>(elementCodec);
