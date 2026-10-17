namespace KeyLoad.Replication;

internal sealed class ReplicaSubmissionBoundary
{
    private const int NotStarted = 0;
    private const int Started = 1;
    private int started;

    internal bool IsStarted => Volatile.Read(ref started) != NotStarted;

    internal void Begin(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref started, Started);
    }
}
