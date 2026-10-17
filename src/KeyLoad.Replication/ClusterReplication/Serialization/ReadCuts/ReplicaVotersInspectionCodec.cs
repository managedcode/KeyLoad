using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;

namespace KeyLoad.Replication;

internal sealed class ReplicaVotersInspectionCodec(IValueSerializer<ImmutableArraySurrogate<string>> surrogate)
    : ReplicaImmutableArrayInspectionCodec<string>(surrogate);
