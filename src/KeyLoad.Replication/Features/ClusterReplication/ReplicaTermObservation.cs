using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>One scalar term from an exact local storage authority and cut.</summary>
internal readonly struct ReplicaTermObservation
{
    internal ReplicaTermObservation(long index, long term, long position, long readGeneration,
        Guid incarnation, Guid nodeId)
    {
        Index = index;
        Term = term;
        Position = position;
        ReadGeneration = readGeneration;
        Incarnation = incarnation;
        NodeId = nodeId;
    }

    internal long Index { get; }
    internal long Term { get; }
    internal long Position { get; }
    internal long ReadGeneration { get; }
    internal Guid Incarnation { get; }
    internal Guid NodeId { get; }
}

internal readonly struct ReplicaTermObservationRead(long term, ReplicaTermObservation observation)
{
    internal long Term { get; } = term;
    internal ReplicaTermObservation Observation { get; } = observation;
}

internal static class ReplicaTermObservationReader
{
    internal static ReplicaTermObservationRead Read(IAtomicStore store, long index, long hardStateTerm,
        ReplicaTermObservation? current)
    {
        return store.Read<ReplicaTermObservationRead>(view =>
        {
            var identity = store.Identity;
            var position = store.Position;
            var readGeneration = identity.ReadGeneration;
            var incarnation = identity.Incarnation;
            var nodeId = identity.NodeId;
            if (current is { } observation
                && observation.Index == index
                && observation.Position == position
                && observation.ReadGeneration == readGeneration
                && observation.Incarnation == incarnation
                && observation.NodeId == nodeId)
            {
                return new(observation.Term, observation);
            }

            long term = 0;
            var found = view.ReadValue(ReplicaProtocol.EntryStorageKey(index), bytes =>
            {
                var entry = ReplicaProtocolCodec.Deserialize<ReplicaEntry>(bytes);
                if (entry.Index != index || entry.Term <= 0 || entry.Term > hardStateTerm)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
                }
                term = entry.Term;
            });
            if (!found)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
            }
            return new(term, new(index, term, position, readGeneration, incarnation, nodeId));
        });
    }
}
