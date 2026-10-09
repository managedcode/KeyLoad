using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class MovementFrameReplicaObservationScope(MovementFrameReplicaObservation owner,
    ReplicaEntry entry, MovementFrameObservationRecord identity) : IDisposable
{
    private const int Open = 0;
    private const int Closed = 1;
    private int disposed;
    private bool rejected;
    private long prefix;
    private int cap;
    internal void Observe(CommitStage stage, long actualPrefix, int actualCap)
    {
        if (stage != CommitStage.EncodedFrameRejected)
        { return; }
        if (rejected || Volatile.Read(ref disposed) != Open)
        { throw new InvalidOperationException(MovementFrameObservationProtocol.Invalid); }
        rejected = true;
        prefix = actualPrefix;
        cap = actualCap;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, Closed) != Open)
        { return; }
        var failures = new List<Exception>();
        if (rejected && prefix == checked((long)cap + MovementFrameObservationProtocol.PrefixStep))
        {
            ServerFailureObserver.Observe(() => owner.Complete(entry,
                identity with { RejectedPrefixBytes = prefix, MaximumFrameBytes = cap }), failures);
        }
        ServerFailureObserver.Observe(() => owner.Exit(this), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
