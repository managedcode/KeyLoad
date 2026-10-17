using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;

namespace KeyLoad.Replication;

internal sealed class ReplicaEntriesInspectionCodec(IValueSerializer<ImmutableArraySurrogate<ReplicaEntry>> surrogate)
    : ReplicaImmutableArrayInspectionCodec<ReplicaEntry>(surrogate);
