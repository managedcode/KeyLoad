using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests;

/// <summary>Counts actual native commit completions without assuming an MVCC timestamp stride.</summary>
internal sealed class NativePublicationObservation
{
    private int count;
    internal int Count => Volatile.Read(ref count);
    internal long Position { get; private set; }
    internal long WriteIdentity { get; private set; }

    internal void Observe(CommitStage stage, long position, int _)
    {
        if (stage == CommitStage.MutationApplied)
        {
            WriteIdentity = position;
        }
        if (stage == CommitStage.ApplyCompleted)
        {
            Interlocked.Increment(ref count);
            Position = position;
        }
    }
}
