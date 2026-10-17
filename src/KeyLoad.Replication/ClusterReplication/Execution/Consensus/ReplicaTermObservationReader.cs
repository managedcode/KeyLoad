using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaTermObservationReader
{
    private const int UnelectedTerm = 0;

    internal static ReplicaTermObservationRead Read(IAtomicStore store, long index, long hardStateTerm, int maximumEntries,
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

            long term = UnelectedTerm;
            var found = view.ReadValue(ReplicaProtocol.EntryStorageKey(index), bytes =>
            {
                var entry = ReplicaProtocolCodec.DeserializeStored<ReplicaEntry>(bytes.ToArray(), maximumEntries);
                if (entry.Index != index || entry.Term <= UnelectedTerm || entry.Term > hardStateTerm)
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
