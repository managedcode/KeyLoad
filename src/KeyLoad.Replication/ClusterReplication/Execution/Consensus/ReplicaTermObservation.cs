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

    internal long Index
    {
        get;
    }
    internal long Term
    {
        get;
    }
    internal long Position
    {
        get;
    }
    internal long ReadGeneration
    {
        get;
    }
    internal Guid Incarnation
    {
        get;
    }
    internal Guid NodeId
    {
        get;
    }
}
